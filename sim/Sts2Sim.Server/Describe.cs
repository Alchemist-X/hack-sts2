using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

// Small, exception-tolerant helpers shared by the token mapper and the observation builder.
public static class Describe
{
    public static string Snake(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string CardName(CardInstance card)
        => card.UpgradeLevel switch
        {
            0 => card.Id,
            1 => card.Id + "+",
            var n => card.Id + "+" + n.ToString(CultureInfo.InvariantCulture)
        };

    public static string OptionName(ChoiceOption option)
        => option.UpgradeLevel switch
        {
            0 => option.ModelId,
            1 => option.ModelId + "+",
            var n => option.ModelId + "+" + n.ToString(CultureInfo.InvariantCulture)
        };

    public static SortedDictionary<string, int> Vars(CardInstance card)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var all = card.Vars.All;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                result[Snake(v.Name)] = v.IntValue;
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    public static string VarSummary(CardInstance card)
    {
        var vars = Vars(card);
        var parts = new List<string>(vars.Count);
        foreach (var (name, value) in vars)
        {
            parts.Add(name switch
            {
                "damage" => value.ToString(CultureInfo.InvariantCulture) + " dmg",
                "block" => value.ToString(CultureInfo.InvariantCulture) + " block",
                _ => name + "=" + value.ToString(CultureInfo.InvariantCulture)
            });
        }
        return string.Join(", ", parts);
    }

    public static JsonNode CostNode(CombatState? cs, CardInstance card)
    {
        try
        {
            if (card.EnergyCost.CostsX)
            {
                return JsonValue.Create("X")!;
            }
            int cost = cs != null ? card.EnergyCost.GetAmountToSpend(cs, card) : card.EnergyCost.GetAmountToSpend();
            return JsonValue.Create(cost)!;
        }
        catch (Exception)
        {
            return JsonValue.Create(card.EnergyCost.Canonical)!;
        }
    }

    public static string CostText(CombatState? cs, CardInstance card)
    {
        var node = CostNode(cs, card);
        return node.GetValueKind() == JsonValueKind.String ? "Xe" : node.GetValue<int>().ToString(CultureInfo.InvariantCulture) + "e";
    }

    public static int? StarCost(CombatState? cs, CardInstance card)
    {
        try
        {
            if (card.Def.CanonicalStarCost < 0 && !card.Def.HasStarCostX)
            {
                return null;
            }
            return cs != null ? card.GetStarCostWithModifiers(cs) : card.CurrentStarCost;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static List<string> Keywords(CombatState? cs, CardInstance card)
    {
        var list = new List<string>();
        try
        {
            foreach (var k in card.Keywords(cs).OrderBy(k => k))
            {
                list.Add(Snake(k.ToString()));
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    public static bool Playable(CombatState cs, CardInstance card, out string? reason)
    {
        try
        {
            bool ok = card.CanPlay(cs, out var why);
            if (ok && why == UnplayableReason.None)
            {
                reason = null;
                return true;
            }
            reason = why == UnplayableReason.None ? "unplayable" : why.ToString();
            return false;
        }
        catch (Exception ex)
        {
            reason = "error: " + ex.GetType().Name;
            return false;
        }
    }

    public static List<(int Index, MonsterState Monster)> LiveEnemies(CombatState cs)
    {
        var list = new List<(int, MonsterState)>();
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m.IsEnemy && m.IsAlive)
            {
                list.Add((i, m));
            }
        }
        return list;
    }

    public static List<IntentProjection> Intents(CombatState cs, MonsterState m)
    {
        var list = new List<IntentProjection>();
        try
        {
            var move = m.HasAi ? m.Ai.NextMove : null;
            if (move == null)
            {
                return list;
            }
            var targets = MonsterLifecycle.PlayerCreatures(cs);
            foreach (var intent in move.Intents)
            {
                try
                {
                    list.Add(SimProjector.Intent(cs, m, intent, targets));
                }
                catch (Exception)
                {
                    list.Add(new IntentProjection { Type = intent.IntentType.ToString() });
                }
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    public static string IntentText(IntentProjection intent)
    {
        if (intent.AttackValue is { } dmg)
        {
            int times = intent.AttackTimes ?? 1;
            return times > 1
                ? $"{intent.Type} {dmg}x{times}"
                : $"{intent.Type} {dmg}";
        }
        if (intent.Extras.TryGetValue(SimProjector.StatusCardCountKey, out var count) && count != null)
        {
            return $"{intent.Type}({count} cards)";
        }
        return intent.Type;
    }

    public static List<(string Id, int Amount)> Powers(CombatState cs, CreatureState creature)
    {
        var list = new List<(string, int)>();
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            if (IsShown(cs, power))
            {
                list.Add((power.Id, DisplayAmount(cs, power, out _)));
            }
        }
        return list;
    }

    // Whether the game draws this power's icon (PowerModel.IsVisible: IsVisibleInternal, and the target is nobody,
    // the player or an enemy), via PlayerView.IsVisibleToPlayer. Hidden powers (AMBERGRIS_POWER) are never shown.
    // A failing check hides the power (fail closed: never leak).
    public static bool IsShown(CombatState cs, PowerModel power)
    {
        try
        {
            return PlayerView.IsVisibleToPlayer(cs, power);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> HiddenPowerIds = new(StringComparer.Ordinal);

    // True for a power id whose model is never visible (IsVisibleInternal false, e.g. AMBERGRIS_POWER): the step log
    // leaves its history entries out.
    public static bool IsHiddenPowerId(string id)
        => HiddenPowerIds.GetOrAdd(id, static k =>
        {
            try
            {
                return !PowerRegistry.Create(k).IsVisible;
            }
            catch (Exception)
            {
                return false;
            }
        });

    // Powers whose game DisplayAmount override has no Core port (PlayerView.PowersWithDisplayAmountOverride minus the
    // ones DisplayAmountToPlayer implements): the icon number falls back to the kernel Amount, flagged known = false.
    private static readonly HashSet<string> UnportedDisplayAmount = new(StringComparer.Ordinal) { "CACOPHONY_POWER", "TAG_TEAM_POWER" };

    // The number the game's power icon shows (PowerModel.DisplayAmount), via PlayerView.DisplayAmountToPlayer (the
    // shared Server/Vec rule: HARDENED_SHELL remaining cap, SLOW %, TENDER / SLOTH cards played this turn, AUTOMATION,
    // FERAL, ORBIT, PANACHE, PALE_BLUE_DOT, WITHERING_PRESENCE, MONOLOGUE counters, else the amount). known = false
    // when no port of the override exists (the kernel Amount is shown).
    public static int DisplayAmount(CombatState cs, PowerModel power, out bool known)
    {
        known = !UnportedDisplayAmount.Contains(power.Id);
        try
        {
            return PlayerView.DisplayAmountToPlayer(cs, power);
        }
        catch (Exception)
        {
            known = false;
            return power.Amount;
        }
    }

    public static string PowersText(IEnumerable<(string Id, int Amount)> powers)
        => string.Join(", ", powers.Select(p => p.Id + " " + p.Amount.ToString(CultureInfo.InvariantCulture)));

    // "ID (uN)": the monster at cs.Monsters[index] (the index a token uses) with its stable uid.
    public static string MonsterName(CombatState cs, int index)
        => index >= 0 && index < cs.Monsters.Count ? $"{cs.Monsters[index].MonsterId} ({Uid(cs.Monsters[index])})" : "?";

    public static string DeckSummary(IReadOnlyList<CardInstance> deck)
    {
        var counts = new List<(string Name, int Count)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < deck.Count; i++)
        {
            string name = CardName(deck[i]);
            if (index.TryGetValue(name, out int k))
            {
                counts[k] = (name, counts[k].Count + 1);
            }
            else
            {
                index[name] = counts.Count;
                counts.Add((name, 1));
            }
        }
        return string.Join(", ", counts.Select(c => c.Count > 1 ? $"{c.Name} x{c.Count}" : c.Name));
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    // Stable creature id for the whole combat: the kernel's CombatId (CombatState.AllocateCreatureId, never reused).
    public static string Uid(CreatureState c) => "u" + I(c.CombatId);

    // "player" for a player, "uN ID" for a monster (enemy or pet).
    public static string Label(CreatureState c) => c is MonsterState m ? Uid(m) + " " + m.MonsterId : "player";

    public static string? MoveId(MonsterState m)
    {
        try
        {
            return m.HasAi ? m.Ai.NextMove?.Id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Visible powers as models (same filter as Powers).
    public static List<PowerModel> PowerModels(CombatState cs, CreatureState creature)
    {
        var list = new List<PowerModel>();
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (IsShown(cs, powers[i]))
            {
                list.Add(powers[i]);
            }
        }
        return list;
    }

    // SURROUNDED fights (Powers/Defs/SurroundedPower.cs): the player faces right or left; an enemy with
    // BACK_ATTACK_LEFT (RIGHT) stands on that side; attacks from behind deal x1.5 (SurroundedPower.BackAttackMultiplier).
    public static string? Facing(CreatureState player)
        => player.GetPower<SurroundedPower>() is { } s ? (s.Facing == SurroundedPower.Direction.Right ? "right" : "left") : null;

    public static string? Flank(CreatureState m)
        => m.HasPower<BackAttackLeftPower>() ? "left" : m.HasPower<BackAttackRightPower>() ? "right" : null;

    // True when this enemy's attacks hit the player's back now (SurroundedPower.ModifyDamageMultiplicative).
    public static bool? Behind(CombatState cs, CreatureState m)
    {
        var s = cs.Player.GetPower<SurroundedPower>();
        if (s == null || Flank(m) == null)
        {
            return null;
        }
        return s.Facing == SurroundedPower.Direction.Right ? m.HasPower<BackAttackLeftPower>() : m.HasPower<BackAttackRightPower>();
    }

    private static string? CreatureLabelById(CombatState cs, int id)
    {
        if (id == PowerModel.NoCreature)
        {
            return null;
        }
        var c = cs.GetCreatureById(id);
        return c != null ? Label(c) : "u" + I(id);
    }

    // What the player can see of a power beyond its icon number, or null. Only facts the game shows: the facing,
    // the cards a power binds or downgrades (marked on the cards themselves), the creature a power targets, the
    // icon counter of SLOTH / TENDER / WITHERING_PRESENCE, SLOW's bonus and THE_BOMB's damage (in their
    // descriptions), POISON triggers (from visible ACCELERANT). Never the applier or kernel bookkeeping
    // (ApplierId, AmountOnTurnStart, SkipNextDurationTick).
    public static string? PowerDetail(CombatState cs, PowerModel power)
    {
        try
        {
            var parts = new List<string>();
            switch (power)
            {
                case SurroundedPower s:
                    parts.Add(s.Facing == SurroundedPower.Direction.Right
                        ? "facing right: left-side enemies hit your back x1.5"
                        : "facing left: right-side enemies hit your back x1.5");
                    break;
                case DampenPower d:
                    parts.Add($"{I(d.DowngradedCards.Count)} cards downgraded until " + string.Join(", ", d.CasterIds.Select(id => CreatureLabelById(cs, id))) + " dies");
                    break;
                case ChainsOfBindingPower c:
                {
                    var bound = BoundCards(power.Owner);
                    parts.Add($"{I(bound.Count)} BOUND cards" + (bound.Count > 0 ? " (" + string.Join(", ", bound.Select(CardName)) + ")" : string.Empty)
                        + "; a BOUND card was played this turn: " + (c.BoundCardPlayed ? "yes" : "no"));
                    break;
                }
                case SlothPower sl:
                    parts.Add($"{I(sl.CardsPlayedThisTurn)}/{I(sl.Amount)} cards played this turn");
                    break;
                case TenderPower t:
                    parts.Add($"{I(t.CardsPlayedThisTurn)} cards played this turn");
                    break;
                case SlowPower sw:
                    parts.Add($"+{I(sw.SlowAmount * 10)}% attack damage taken now");
                    break;
                case WitheringPresencePower w:
                    parts.Add($"{I((int)w.CardsLeft)} cards until WITHER");
                    break;
                case TheBombPower b:
                    parts.Add($"{I((int)b.Damage)} dmg to all enemies");
                    break;
                case PoisonPower p:
                    int triggers = p.TriggerCount(cs);
                    if (triggers > 1)
                    {
                        parts.Add($"triggers {I(triggers)}x");
                    }
                    break;
            }
            if (power.TargetId != PowerModel.NoCreature)
            {
                parts.Add("target " + CreatureLabelById(cs, power.TargetId));
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static List<CardInstance> BoundCards(CreatureState owner)
    {
        var list = new List<CardInstance>();
        if (owner is not PlayerState player)
        {
            return list;
        }
        foreach (var pile in player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                if (card.Affliction?.Id == ChainsOfBindingPower.BoundAfflictionId)
                {
                    list.Add(card);
                }
            }
        }
        return list;
    }

    // Instance ids of the cards a DAMPEN power downgraded (restored when its casters die).
    public static HashSet<int> DampenedCardIds(CombatState? cs)
    {
        var set = new HashSet<int>();
        if (cs?.Player.GetPower<DampenPower>() is { } d)
        {
            foreach (var (id, _) in d.DowngradedCards)
            {
                set.Add(id);
            }
        }
        return set;
    }

    // Effective numbers through the kernel's preview path (Combat/Pipeline/CardPreview.cs = the game's
    // DynamicVar.UpdateCardPreview): per-hit damage against each live enemy (by uid) and block.
    public sealed record Effective(SortedDictionary<string, int>? Damage, int? Times, int? Block);

    public static Effective? EffectiveNumbers(CombatState cs, CardInstance card)
    {
        try
        {
            SortedDictionary<string, int>? damage = null;
            var vars = card.Vars;
            bool hasDamage = vars.TryGet(DamageVar.DefaultName) != null || vars.TryGet(CalculatedDamageVar.DefaultName) != null || vars.TryGet(OstyDamageVar.DefaultName) != null;
            if (hasDamage)
            {
                damage = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var (_, m) in LiveEnemies(cs))
                {
                    if (CardPreview.Damage(cs, card, m) is { } d)
                    {
                        damage[Uid(m)] = (int)d;
                    }
                }
                if (damage.Count == 0)
                {
                    damage = null;
                }
            }
            int? block = CardPreview.Block(cs, card, null) is { } b ? (int)b : null;
            if (damage == null && block == null)
            {
                return null;
            }
            return new Effective(damage, damage != null ? HitCount(card) : null, block);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Hits per play: the card's Repeat var, else its def's fixed HitCount constant, else 1 (X cost: null). Cards that
    // compute the hit count from other state at play time report 1.
    public static int? HitCount(CardInstance card) => PlayerView.HitCount(card);

    // The card's own energy cost (Base, upgrades included) when the current cost differs from it, else null.
    public static int? BaseCost(CombatState? cs, CardInstance card)
    {
        try
        {
            if (card.EnergyCost.CostsX)
            {
                return null;
            }
            int now = cs != null ? card.EnergyCost.GetAmountToSpend(cs, card) : card.EnergyCost.GetAmountToSpend();
            int baseCost = card.EnergyCost.Base;
            return now != baseCost ? baseCost : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The danger summary of sim/PLAY.md: incoming attack damage projected from the intents vs current block.
    public sealed record Incoming(string Uid, string MonsterId, int Damage, int Times);

    public sealed record Timer(string Source, string Id, string Text);

    public sealed record Danger(List<Incoming> ByEnemy, int Total, int Hits, int Block, int NetHpLoss, int HpAfter, List<Timer> Timers);

    public static Danger DangerOf(CombatState cs)
    {
        var by = new List<Incoming>();
        int total = 0;
        int hits = 0;
        foreach (var (_, m) in LiveEnemies(cs))
        {
            foreach (var intent in Intents(cs, m))
            {
                if (intent.AttackValue is { } dmg)
                {
                    int times = intent.AttackTimes ?? 1;
                    by.Add(new Incoming(Uid(m), m.MonsterId, dmg, times));
                    total += dmg * times;
                    hits += times;
                }
            }
        }
        var p = cs.Player;
        int net = Math.Max(0, total - p.Block);
        return new Danger(by, total, hits, p.Block, net, Math.Max(0, p.CurrentHp - net), Timers(cs));
    }

    // Powers and hand cards that hurt, kill or penalise on a timer or per card played. Each entry's behaviour is read
    // from its kernel port (sim/Sts2Sim.Core/Powers/Defs/<Name>Power.cs; Cards/Defs/<Card>.cs OnTurnEndInHand).
    public static List<Timer> Timers(CombatState cs)
    {
        var list = new List<Timer>();
        var creatures = new List<CreatureState> { cs.Player };
        foreach (var (_, m) in LiveEnemies(cs))
        {
            creatures.Add(m);
        }
        foreach (var c in creatures)
        {
            string who = Label(c);
            string source = c is MonsterState ? Uid(c) : "player";
            foreach (var power in c.Powers)
            {
                string? text;
                try
                {
                    text = TimerText(cs, c, power, who);
                }
                catch (Exception)
                {
                    text = null;
                }
                if (text != null)
                {
                    list.Add(new Timer(source, power.Id, text));
                }
            }
        }
        foreach (var card in cs.Player.Hand.Cards)
        {
            bool endEffect;
            try
            {
                endEffect = card.HasTurnEndInHandEffect;
            }
            catch (Exception)
            {
                endEffect = false;
            }
            if (endEffect)
            {
                string vars = VarSummary(card);
                list.Add(new Timer("hand", card.Id, $"end of turn in hand: {CardName(card)}" + (vars.Length > 0 ? $" ({vars})" : string.Empty)));
            }
        }
        return list;
    }

    private static string? TimerText(CombatState cs, CreatureState c, PowerModel power, string who)
    {
        int a = power.Amount;
        switch (power)
        {
            case PoisonPower p:
            {
                int triggers = p.TriggerCount(cs);
                return $"start of turn: POISON {I(a)} dmg to {who} (unblockable" + (triggers > 1 ? $", {I(triggers)} triggers)" : ")");
            }
            case ConstrictPower:
                return $"end of turn: CONSTRICT {I(a)} dmg to {who}";
            case DemisePower:
                return $"end of turn: DEMISE {I(a)} dmg to {who} (unblockable)";
            case DisintegrationPower:
                return $"end of turn: DISINTEGRATION {I(a)} dmg to {who}";
            case DoomPower:
                return $"end of turn: DOOM {I(a)} kills {who} if HP <= {I(a)} (HP {I(c.CurrentHp)}" + (c.CurrentHp <= a ? ", LETHAL)" : ")");
            case TenderPower:
                return $"TENDER: {who} -1 STR/DEX per card played this turn (restored at end of turn)";
            case StranglePower:
                return $"STRANGLE: {who} takes {I(a)} dmg per card you play this turn (unblockable)";
            case OblivionPower:
                return $"OBLIVION: each card you play this turn gives {who} {I(a)} DOOM";
            case SlowPower s:
                return $"SLOW: {who} takes +10% attack dmg per card played this turn (now +{I(s.SlowAmount * 10)}%)";
            case GalvanicPower:
                return $"GALVANIC: playing a GALVANIZED card deals {I(a)} dmg to you";
            case ChainsOfBindingPower:
                return $"CHAINS_OF_BINDING: up to {I(a)} drawn cards per turn become BOUND; once one BOUND card is played the others cannot be";
            case SlothPower s:
                return $"SLOTH: at most {I(a)} cards per turn ({I(s.CardsPlayedThisTurn)} played)";
            case SmoggyPower:
                return "SMOGGY: after you play a Skill, your other Skills get SMOG (unplayable) until end of turn";
            case SandpitPower:
                return $"countdown: SANDPIT {I(a)} turns - {who} kills you when it runs out (ticks at the start of each enemy turn)";
            case TheBombPower b:
                return $"countdown: THE_BOMB {I(a)} turns - {I((int)b.Damage)} dmg to all enemies";
            case BattlewornDummyTimeLimitPower:
                return $"countdown: {who} escapes in {I(a)} turns";
            case HatchPower:
                return $"countdown: HATCH {I(a)} on {who}";
            case CountdownPower:
                return $"start of turn: {I(a)} DOOM to a random enemy";
            case WitheringPresencePower w:
                return $"WITHERING_PRESENCE: every {I(WitheringPresencePower.BaseCardsLeft)}th card you play adds WITHER to your hand ({I((int)w.CardsLeft)} left)";
            case CrimsonMantlePower m when m.SelfDamage > 0:
                return $"start of turn: CRIMSON_MANTLE {I((int)m.SelfDamage)} dmg to {who}";
            case InfernoPower f when f.SelfDamage > 0:
                return $"start of turn: INFERNO {I((int)f.SelfDamage)} dmg to {who}";
            default:
                return null;
        }
    }
}
