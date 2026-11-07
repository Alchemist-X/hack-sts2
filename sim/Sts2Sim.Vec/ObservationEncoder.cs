using System.Text;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Vec;

/// <summary>
/// Player-view observation encoder (owner: V1). Writes every observation buffer of one slot (<see cref="Buf.Observation"/>),
/// following CONTRACTS.md §D3.3 (hidden-information list H1–H15; field semantics in sim/VEC.md). Does not touch
/// cand / cand_n / signals (VecEnv and CandidateTable own those). Sets <see cref="SlotStatus.TableOverflow"/> in
/// <paramref name="status"/> when a table is truncated to its capacity.
/// Pure: reads the run through public Core API only (no state writes, no RNG draws; the hash is unchanged), and
/// allocation-free apart from Core calls (per-thread scratch).
/// May throw; VecEnv maps exceptions to CONTENT_GAP / SIM_ERROR.
/// </summary>
public static partial class ObservationEncoder
{
    /// <summary>Divisor of global_f.floor_frac: the victory floor without / with the A10 double boss (48 / 49).</summary>
    public const int VictoryFloor = 48;

    private sealed class Scratch
    {
        public readonly List<MapPoint> MapPoints = new(8);

        public readonly CreatureState[] Targets = new CreatureState[1];

        /// <summary>draw_marked rows before sorting (card_id, upgrade, enchant_id, enchant_amount, affliction_id, affliction_amount).</summary>
        public readonly List<(int, int, int, int, int, int)> Marked = new(16);

        /// <summary>reward_choices rows (RewardMacros.Choices).</summary>
        public readonly List<RewardMacros.Choice> RewardChoices = new(16);
    }

    [ThreadStatic]
    private static Scratch? _scratch;

    private static Scratch S => _scratch ??= new Scratch();

    /// <summary>Encodes the player view of <paramref name="sim"/> into slot <paramref name="b"/>.</summary>
    public static void Encode(RunSim sim, in SlotBuffers b, ref SlotStatus status)
    {
        b.ClearObservation();
        var rs = sim.State;
        var gi = b.GlobalI;
        var phase = sim.Phase;
        var prompt = sim.PendingChoice;
        gi[GI.Phase] = (int)phase;
        gi[GI.DecisionKind] = (int)(prompt != null ? RunPhase.Prompt : phase);
        var cs = rs.Combat is { IsInProgress: true } live ? live : null;
        gi[GI.IsCombat] = cs != null ? 1 : 0;
        if (rs.Players.Count == 0)
        {
            return;
        }
        var p = rs.Players[0];
        bool overflow = false;

        EncodeHeader(rs, p, cs, gi);
        if (rs.Acts.Count > 0)
        {
            // The act's Ancient: its portrait marks its map point (NAncientMapPoint), like the bosses (H3).
            b.GlobalX[GX.AncientId] = Vocab.Event(rs.Act.Rooms.AncientId);
        }
        EncodeDeck(p, b.Deck, ref overflow);
        gi[GI.RelicN] = p.Relics.Count;
        EncodeRelics(p, b.Relics, ref overflow);
        EncodePotions(sim, p, cs, prompt != null, b.Potions, ref overflow);
        if (cs != null)
        {
            EncodeCombat(cs, b, gi, ref overflow);
        }
        EncodeMap(sim, rs, b.MapNodes, ref overflow);
        if (rs.Map is { } map)
        {
            EncodeMapRoutes(map, b.MapNodes, b.MapRoutes);
        }
        EncodeScreens(sim, rs, p, b, gi, ref overflow);
        if (prompt != null)
        {
            EncodePrompt(prompt, sim.PendingPicks, b.PromptOptions, gi, ref overflow);
        }
        EncodeDerived(rs, p, cs, b.GlobalF, gi);
        EncodeAux(sim, rs, p, cs != null, b.Aux);
        if (overflow)
        {
            status |= SlotStatus.TableOverflow;
        }
    }

    /// <summary>Observation of a slot that holds no run: every observation buffer zero.</summary>
    public static void EncodeEmpty(in SlotBuffers b) => b.ClearObservation();

    /// <summary>
    /// The run-derived columns of <c>last_episode</c> for the episode that ends at <paramref name="sim"/>: floor, act,
    /// final_hp, final_max_hp, last_encounter_id (the last Monster/Elite/Boss room in the floor history). win, steps and
    /// end_kind are VecEnv's and are left untouched. Pure and allocation-free.
    /// </summary>
    public static void FillEpisodeSummary(RunSim sim, Span<int> lastEpisode)
    {
        var rs = sim.State;
        lastEpisode[LastEpCol.Floor] = rs.TotalFloor;
        lastEpisode[LastEpCol.Act] = rs.Acts.Count > 0 ? rs.CurrentActIndex : 0;
        if (rs.Players.Count > 0)
        {
            lastEpisode[LastEpCol.FinalHp] = rs.Players[0].CurrentHp;
            lastEpisode[LastEpCol.FinalMaxHp] = rs.Players[0].MaxHp;
        }
        lastEpisode[LastEpCol.LastEncounterId] = 0;
        var history = rs.MapPointHistory;
        for (int h = history.Count - 1; h >= 0; h--)
        {
            var rooms = history[h].Rooms;
            for (int r = rooms.Count - 1; r >= 0; r--)
            {
                if (rooms[r].RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
                {
                    lastEpisode[LastEpCol.LastEncounterId] = Vocab.Encounter(rooms[r].ModelId);
                    return;
                }
            }
        }
    }

    // ---- run header -----------------------------------------------------------------------------------------------

    private static void EncodeHeader(RunState rs, PlayerState p, CombatState? cs, Span<int> gi)
    {
        gi[GI.Character] = Vocab.Character(p.CharacterId);
        gi[GI.Ascension] = rs.Ascension;
        if (rs.Acts.Count > 0)
        {
            var act = rs.Act;
            gi[GI.ActIndex] = rs.CurrentActIndex;
            gi[GI.ActId] = Vocab.Act(act.Def.Id);
            // H3: only the current act's bosses (shown on the map and the top bar).
            gi[GI.BossId] = Vocab.Encounter(act.Rooms.Boss?.Id);
            gi[GI.SecondBossId] = Vocab.Encounter(act.Rooms.SecondBoss?.Id);
        }
        gi[GI.TotalFloor] = rs.TotalFloor;
        gi[GI.ActFloor] = rs.ActFloor;
        if (rs.CurrentMapCoord is { } coord)
        {
            gi[GI.MapRow] = coord.row;
            gi[GI.MapCol] = coord.col;
        }
        else
        {
            gi[GI.MapRow] = -1;
            gi[GI.MapCol] = -1;
        }
        gi[GI.Hp] = p.CurrentHp;
        gi[GI.MaxHp] = p.MaxHp;
        gi[GI.Gold] = p.Gold;
        gi[GI.Stars] = p.Stars;
        gi[GI.PotionSlots] = p.MaxPotionCount;
        gi[GI.DeckN] = p.Deck.Count;
        gi[GI.CardRemovalsUsed] = p.ExtraFields.CardShopRemovalsUsed;
        gi[GI.RemovalPrice] = MerchantCardRemovalEntry.BaseCost(rs.Ascension)
                              + MerchantCardRemovalEntry.PriceIncrease(rs.Ascension) * p.ExtraFields.CardShopRemovalsUsed;
        if (cs != null)
        {
            var cp = cs.Player;
            gi[GI.Energy] = cp.Energy;
            gi[GI.MaxEnergy] = EnergyCmd.GetMaxEnergy(cs, cp);
            gi[GI.Block] = cp.Block;
            gi[GI.Turn] = cp.TurnNumber;
            gi[GI.Round] = cs.RoundNumber;
            gi[GI.OrbSlots] = cp.OrbQueue.Capacity;
            gi[GI.DrawN] = cp.DrawPile.Count;
            gi[GI.DiscardN] = cp.DiscardPile.Count;
            gi[GI.ExhaustN] = cp.ExhaustPile.Count;
            gi[GI.HandN] = cp.Hand.Count;
        }
        else
        {
            gi[GI.MaxEnergy] = p.BaseMaxEnergy;
            gi[GI.OrbSlots] = p.BaseOrbSlotCount;
        }
    }

    private static void EncodeDerived(RunState rs, PlayerState p, CombatState? cs, Span<float> gf, Span<int> gi)
    {
        gf[GF.HpFrac] = p.MaxHp > 0 ? (float)p.CurrentHp / p.MaxHp : 0f;
        gf[GF.GoldLog1p] = MathF.Log(1f + Math.Max(0, p.Gold));
        int victoryFloor = rs.HasAscension(AscensionLevel.DoubleBoss) ? VictoryFloor + 1 : VictoryFloor;
        gf[GF.FloorFrac] = (float)rs.TotalFloor / victoryFloor;
        int block = cs != null ? cs.Player.Block : 0;
        gf[GF.IncomingNetHp] = p.CurrentHp - Math.Max(0, gi[GI.IncomingDamage] - block);
    }

    private static void EncodeAux(RunSim sim, RunState rs, PlayerState p, bool inCombat, Span<float> aux)
    {
        aux[AuxCol.TotalFloor] = rs.TotalFloor;
        aux[AuxCol.ActIndex] = rs.Acts.Count > 0 ? rs.CurrentActIndex : 0;
        aux[AuxCol.Hp] = p.CurrentHp;
        aux[AuxCol.MaxHp] = p.MaxHp;
        aux[AuxCol.Gold] = p.Gold;
        CountCombats(sim, rs, inCombat, out int won, out int bosses);
        aux[AuxCol.BossesKilled] = bosses;
        aux[AuxCol.CombatsWon] = won;
        aux[AuxCol.DeckN] = p.Deck.Count;
        aux[AuxCol.RelicN] = p.Relics.Count;
        aux[AuxCol.IsCombat] = inCombat ? 1f : 0f;
        // Protocol v0.2 progress inputs (docs/sts2-bench-protocol-v0.2-2026-11-01.md §4.2): the act's start floor is
        // total_floor - act_floor, and the act's room count L is the current act map's last row + 1.
        aux[AuxCol.ActFloor] = rs.ActFloor;
        if (rs.CurrentMapCoord is { } coord)
        {
            aux[AuxCol.MapRow] = coord.row;
            aux[AuxCol.MapCol] = coord.col;
        }
        else
        {
            aux[AuxCol.MapRow] = -1;
            aux[AuxCol.MapCol] = -1;
        }
        aux[AuxCol.ActRooms] = ActRooms(rs.Map);
    }

    /// <summary>Rooms in the act of <paramref name="map"/> (last row + 1: start row 0 through the last boss row); 0 without a map.</summary>
    public static int ActRooms(ActMap? map)
    {
        if (map == null)
        {
            return 0;
        }
        int last = map.StartingMapPoint.coord.row;
        if (map.BossMapPoint is { } boss)
        {
            last = Math.Max(last, boss.coord.row);
        }
        if (map.SecondBossMapPoint is { } second)
        {
            last = Math.Max(last, second.coord.row);
        }
        int rows = map.GetRowCount();
        int cols = map.GetColumnCount();
        for (int r = rows - 1; r > last; r--)
        {
            for (int c = 0; c < cols; c++)
            {
                if (map.GetPoint(c, r) != null)
                {
                    last = r;
                    break;
                }
            }
        }
        return last + 1;
    }

    // Combat rooms in the floor history; the current floor's last room does not count while its combat is still
    // running or when the run was lost in it.
    private static void CountCombats(RunSim sim, RunState rs, bool inCombat, out int won, out int bosses)
    {
        won = 0;
        bosses = 0;
        var history = rs.MapPointHistory;
        bool lastUnfinished = false;
        if (history.Count > 0 && (inCombat || sim.Loop.Phase == RunPhase.Defeat))
        {
            var lastRooms = history[^1].Rooms;
            lastUnfinished = lastRooms.Count > 0 && lastRooms[^1].RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss;
        }
        bool skippedLast = false;
        for (int h = history.Count - 1; h >= 0; h--)
        {
            var rooms = history[h].Rooms;
            for (int r = rooms.Count - 1; r >= 0; r--)
            {
                var type = rooms[r].RoomType;
                if (type is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
                {
                    continue;
                }
                if (lastUnfinished && !skippedLast)
                {
                    skippedLast = true;
                    continue;
                }
                won++;
                if (type == RoomType.Boss)
                {
                    bosses++;
                }
            }
        }
    }

    // ---- inventory ------------------------------------------------------------------------------------------------

    private static void EncodeDeck(PlayerState p, Span<int> deck, ref bool overflow)
    {
        const int w = 5;
        var cards = p.Deck;
        int n = cards.Count;
        if (n > Caps.Deck)
        {
            overflow = true;
            n = Caps.Deck;
        }
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            var row = deck.Slice(i * w, w);
            row[0] = Vocab.Card(card.Id);
            row[1] = card.UpgradeLevel;
            if (card.Enchantment is { } ench)
            {
                row[2] = Vocab.Enchantment(ench.Id);
                row[3] = ench.Amount;
            }
            row[4] = Vocab.Affliction(card.Affliction?.Id);
        }
    }

    private static void EncodeRelics(PlayerState p, Span<int> relics, ref bool overflow)
    {
        const int w = 5;
        var list = p.Relics;
        int n = list.Count;
        if (n > Caps.Relics)
        {
            overflow = true;
            n = Caps.Relics;
        }
        for (int i = 0; i < n; i++)
        {
            var relic = list[i];
            var row = relics.Slice(i * w, w);
            row[0] = Vocab.Relic(relic.Id);
            row[1] = relic.ShowCounter ? relic.DisplayAmount : -1;
            row[2] = (int)relic.Status;
            row[3] = relic.IsUsedUp ? 1 : 0;
            row[4] = relic.StackCount;
        }
    }

    private static void EncodePotions(RunSim sim, PlayerState p, CombatState? cs, bool atPrompt, Span<int> potions, ref bool overflow)
    {
        const int w = 3;
        var slots = p.PotionSlots;
        int n = slots.Length;
        if (n > Caps.Potions)
        {
            overflow = true;
            n = Caps.Potions;
        }
        var loopPhase = sim.Loop.Phase;
        for (int i = 0; i < n; i++)
        {
            var potion = slots[i];
            if (potion == null)
            {
                continue;
            }
            var row = potions.Slice(i * w, w);
            row[0] = Vocab.Potion(potion.Id);
            bool usable = false;
            if (!atPrompt)
            {
                if (cs != null)
                {
                    usable = CanUsePotionInCombat(cs, i);
                }
                else if (loopPhase is not (RunPhase.Combat or RunPhase.Victory or RunPhase.Defeat))
                {
                    usable = RunLegalActionGenerator.CanUsePotionOutOfCombat(p, i);
                }
            }
            row[1] = usable ? 1 : 0;
            row[2] = (int)potion.TargetType;
        }
    }

    // The combat potion rule of LegalActionGenerator.FillPotions: a UsePotion action exists for this slot.
    private static bool CanUsePotionInCombat(CombatState cs, int slot)
    {
        if (!LegalActionGenerator.IsAtDecisionPoint(cs))
        {
            return false;
        }
        var player = cs.Player;
        if (player.Phase != PlayerTurnPhase.Play || player.IsDead || !player.CanUseOrRemovePotions)
        {
            return false;
        }
        var potion = player.PotionSlots[slot];
        if (potion == null || potion.Usage is not (PotionUsage.CombatOnly or PotionUsage.AnyTime))
        {
            return false;
        }
        var self = LegalActionGenerator.IsSingleTarget(potion.TargetType) ? (CreatureState)player : null;
        if (PotionActions.CanUse(cs, slot, self))
        {
            return true;
        }
        var monsters = cs.Monsters;
        for (int t = 0; t < monsters.Count; t++)
        {
            if (PotionActions.CanUse(cs, slot, monsters[t]))
            {
                return true;
            }
        }
        var allies = cs.Allies;
        for (int a = 1; a < allies.Count; a++)
        {
            if (PotionActions.CanUse(cs, slot, allies[a]))
            {
                return true;
            }
        }
        return false;
    }

    // ---- cards ----------------------------------------------------------------------------------------------------

    /// <summary>HC columns of one card. In combat (<paramref name="cs"/> set) costs and keywords include the combat
    /// modifiers and the playability columns are written; out of combat (card rewards) they stay 0.</summary>
    private static void WriteCard(Span<int> row, CardInstance card, CombatState? cs)
    {
        row[HC.CardId] = Vocab.Card(card.Id);
        row[HC.Upgrade] = card.UpgradeLevel;
        var ec = card.EnergyCost;
        if (ec.CostsX)
        {
            row[HC.CostCurrent] = HC.CostX;
            row[HC.CostBase] = HC.CostX;
        }
        else if (ec.Base < 0)
        {
            row[HC.CostCurrent] = HC.CostUnplayable;
            row[HC.CostBase] = HC.CostUnplayable;
        }
        else
        {
            row[HC.CostCurrent] = cs != null ? PlayerView.CostOf(cs, card).Value : ec.GetAmountToSpend();
            row[HC.CostBase] = ec.Base;
        }
        row[HC.Type] = (int)card.Type;
        row[HC.Target] = (int)card.TargetType;
        row[HC.Rarity] = (int)card.Rarity;
        row[HC.KeywordMask] = card.GetKeywordMaskWithSources(cs, KeywordSources.All);
        if (card.Enchantment is { } ench)
        {
            row[HC.EnchantId] = Vocab.Enchantment(ench.Id);
            row[HC.EnchantAmount] = ench.Amount;
        }
        row[HC.AfflictionId] = Vocab.Affliction(card.Affliction?.Id);
        if (cs != null)
        {
            bool ok = card.CanPlay(cs, out var reason);
            row[HC.Playable] = ok && reason == UnplayableReason.None ? 1 : 0;
            row[HC.PlayableReasonId] = (int)reason;
        }
        var vars = card.Vars.All;
        int nv = Math.Min(vars.Count, Caps.HandVars);
        for (int k = 0; k < nv; k++)
        {
            row[HC.VarId0 + k] = Vocab.CardVar(vars[k].Name);
            row[HC.VarVal0 + k] = vars[k].IntValue;
        }
    }

    internal static bool HasDamageVar(CardInstance card)
    {
        var vars = card.Vars;
        return vars.TryGet(DynamicVarSet.DamageName) != null
               || vars.TryGet(DynamicVarSet.CalculatedDamageName) != null
               || vars.TryGet(DynamicVarSet.OstyDamageName) != null;
    }

    /// <summary>Hash id for strings without a vocabulary (prompt sites, event option keys, bundles): the low 32 bits of
    /// XXH64 (seed 0) of the UTF-8 of <paramref name="a"/>, or of <c>a + ":" + b</c> when <paramref name="b"/> is given.</summary>
    public static int HashText(string a, string? b = null)
    {
        int len = Encoding.UTF8.GetByteCount(a) + (b != null ? 1 + Encoding.UTF8.GetByteCount(b) : 0);
        Span<byte> buf = len <= 512 ? stackalloc byte[len] : new byte[len];
        int n = Encoding.UTF8.GetBytes(a, buf);
        if (b != null)
        {
            buf[n++] = (byte)':';
            Encoding.UTF8.GetBytes(b, buf[n..]);
        }
        return unchecked((int)(uint)DeterministicHash.XxHash64(buf));
    }
}
