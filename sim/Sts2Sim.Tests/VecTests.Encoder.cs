using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Server;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Player-view observation encoder tests (CONTRACTS.md §D7 T2/T3, owner V1): purity, the draw-order leak test, parity
/// with the play server's observation, no move ids, hidden treasure / card-reward contents, table capacities.
/// </summary>
public static class VecEncoderTests
{
    private static readonly string[] Characters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    /// <summary>
    /// Uniform-random legal playouts (PortedOnly). <paramref name="visit"/> sees every decision point of every run
    /// (live state: clone to keep it) and returns false to stop everything. With <paramref name="heal"/> the player is
    /// healed to full (max HP raised to at least 200) at non-prompt points below half HP, so runs reach deep floors.
    /// <paramref name="prefer"/> may pick an action index instead of the random one (-1 = random).
    /// </summary>
    private static void Playouts(int runs, int maxSteps, int seed, bool heal, Func<RunSim, bool> visit, Func<RunSim, IReadOnlyList<RunAction>, int>? prefer = null)
    {
        for (int r = 0; r < runs; r++)
        {
            var rng = new System.Random(seed * 7919 + r);
            string character = Characters[r % Characters.Length];
            var sim = RunSim.NewRun(new RunConfig($"VENC-{seed}-{r}", character, r % 3 == 0 ? 10 : 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < maxSteps && !sim.IsTerminal; step++)
            {
                if (heal && !sim.HasPendingChoice && sim.State.Players[0] is { } p && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 200);
                    p.CurrentHp = p.MaxHp;
                }
                if (!visit(sim))
                {
                    return;
                }
                var legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                int pick = prefer?.Invoke(sim, legal) ?? -1;
                var action = legal[pick >= 0 ? pick : rng.Next(legal.Count)];
                try
                {
                    sim.Step(action);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
    }

    private static void EncodeInto(RunSim sim, NativeBuffers nb, out SlotStatus status)
    {
        status = SlotStatus.Ok;
        ObservationEncoder.Encode(sim, nb.Slot(0), ref status);
    }

    private static string? FirstDifference(NativeBuffers a, NativeBuffers b)
    {
        foreach (int k in Buf.Observation)
        {
            var x = a.AllBytes(k);
            var y = b.AllBytes(k);
            if (!x.SequenceEqual(y))
            {
                int at = 0;
                while (at < x.Length && x[at] == y[at])
                {
                    at++;
                }
                return $"{Schema.Buffers[k].Name} (byte {at})";
            }
        }
        return null;
    }

    public static void Test_VecEncoder_IsPure()
    {
        using var nb = new NativeBuffers(1);
        using var nb2 = new NativeBuffers(1);
        int checkedStates = 0;
        int combat = 0;
        int prompts = 0;
        int overflows = 0;
        var phases = new HashSet<RunPhase>();
        int visits = 0;
        int unknown = 0;
        long ticks = 0;
        long allocated = 0;
        Playouts(60, 1500, 11, heal: true, sim =>
        {
            if (visits++ % 7 != 0)
            {
                return true;
            }
            ulong before = sim.StateHash();
            EncodeInto(sim, nb, out var st);
            ulong after = sim.StateHash();
            Assert.Equal(before, after, $"state hash changed by encoding (phase {sim.Phase})");
            EncodeInto(sim, nb2, out var st2);
            Assert.Equal(st, st2);
            Assert.Null(FirstDifference(nb, nb2), "encoding twice gives the same bytes");
            Assert.Equal((int)sim.Phase, nb.All<int>(Buf.GlobalI)[GI.Phase]);
            checkedStates++;
            combat += sim.State.Combat is { IsInProgress: true } ? 1 : 0;
            prompts += sim.HasPendingChoice ? 1 : 0;
            overflows += st.HasFlag(SlotStatus.TableOverflow) ? 1 : 0;
            phases.Add(sim.HasPendingChoice ? RunPhase.Prompt : sim.Phase);
            unknown += CountUnknownIds(nb);
            long bytes0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            EncodeInto(sim, nb2, out _);
            ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes0;
            return checkedStates < 1000;
        });
        Console.WriteLine($"     encoder purity: {checkedStates} states ({combat} combat, {prompts} prompts, {overflows} with TABLE_OVERFLOW), phases {string.Join(",", phases.OrderBy(x => x))}; "
            + $"encode {ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, checkedStates):F1} us, {allocated / Math.Max(1, checkedStates)} B allocated per call (Core calls included)");
        Assert.True(checkedStates >= 1000, $"only {checkedStates} states sampled");
        Assert.True(phases.Count >= 6, "the sample covers most run phases");
        Assert.Equal(0, unknown, "ids outside the vocabularies (\"?\") in id columns");
    }

    // Id columns holding VocabList.Unknown (an id the registries do not list).
    private static int CountUnknownIds(NativeBuffers nb)
    {
        int n = 0;
        void Col(int buffer, int width, int col)
        {
            var t = nb.All<int>(buffer);
            for (int r = 0; r * width < t.Length; r++)
            {
                n += t[r * width + col] == VocabList.Unknown ? 1 : 0;
            }
        }
        Col(Buf.Hand, Schema.HC, HC.CardId);
        Col(Buf.Hand, Schema.HC, HC.EnchantId);
        Col(Buf.Hand, Schema.HC, HC.AfflictionId);
        Col(Buf.Deck, 5, 0);
        Col(Buf.Deck, 5, 2);
        Col(Buf.Relics, 5, 0);
        Col(Buf.Potions, 3, 0);
        Col(Buf.PowersPlayer, 3, 0);
        Col(Buf.Orbs, 3, 0);
        Col(Buf.Enemies, 9, 2);
        Col(Buf.EnemyPowers, 2, 0);
        Col(Buf.Allies, 5, 1);
        Col(Buf.AllyPowers, 2, 0);
        Col(Buf.CardReward, Schema.HC, HC.CardId);
        Col(Buf.Shop, 7, 1);
        Col(Buf.Rest, 2, 0);
        Col(Buf.EventOptions, 6, 5);
        var gi = nb.All<int>(Buf.GlobalI);
        foreach (int c in new[] { GI.Character, GI.ActId, GI.BossId, GI.SecondBossId, GI.EventId, GI.TreasureRelicId })
        {
            n += gi[c] == VocabList.Unknown ? 1 : 0;
        }
        for (int r = 0; r < Caps.Hand; r++)
        {
            for (int k = 0; k < Caps.HandVars; k++)
            {
                n += nb.All<int>(Buf.Hand)[r * Schema.HC + HC.VarId0 + k] == VocabList.Unknown ? 1 : 0;
            }
        }
        return n;
    }

    public static void Test_VecEncoder_LeakTest_DrawOrder()
    {
        using var nb = new NativeBuffers(1);
        using var nb2 = new NativeBuffers(1);
        int tested = 0;
        int visits = 0;
        Playouts(80, 1200, 23, heal: true, sim =>
        {
            if (visits++ % 3 != 0 || sim.HasPendingChoice || sim.State.Combat is not { IsInProgress: true } cs || cs.Player.DrawPile.Count < 2)
            {
                return true;
            }
            var clone = sim.Clone();
            var cards = clone.State.Combat!.Player.DrawPile.MutableCards;
            var shuffle = new System.Random(visits);
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = shuffle.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
            if (clone.StateHash() == sim.StateHash())
            {
                return true; // the shuffle kept the order (or only swapped identical copies with equal ids? never: ids differ)
            }
            EncodeInto(sim, nb, out var st);
            EncodeInto(clone, nb2, out var st2);
            Assert.Equal(st, st2);
            Assert.Null(FirstDifference(nb, nb2), "the observation must not depend on the draw-pile order (H1)");
            tested++;
            return tested < 600;
        });
        Console.WriteLine($"     leak test: {tested} combat decision points with a reshuffled draw pile, identical observations");
        Assert.True(tested >= 500, $"only {tested} reshuffled combat states");
    }

    public static void Test_VecEncoder_ParityWithServer()
    {
        using var nb = new NativeBuffers(1);
        var cardVocab = Vocab.Cards;
        int compared = 0;
        int combat = 0;
        int damageChecks = 0;
        int visits = 0;
        Playouts(60, 1500, 37, heal: true, sim =>
        {
            if (visits++ % 9 != 0)
            {
                return true;
            }
            EncodeInto(sim, nb, out _);
            var gi = nb.All<int>(Buf.GlobalI);
            var g = GameSession.ForRun("parity", null, new JsonObject(), sim.Clone());
            var obs = ObservationBuilder.Build(g, ActionTokens.Enumerate(g));
            var player = obs["player"]!;
            string where = $"state {compared} (phase {sim.Phase})";
            Assert.Equal(player["hp"]!.GetValue<int>(), gi[GI.Hp], where + " hp");
            Assert.Equal(player["max_hp"]!.GetValue<int>(), gi[GI.MaxHp], where + " max_hp");
            Assert.Equal(player["gold"]!.GetValue<int>(), gi[GI.Gold], where + " gold");
            Assert.Equal(player["max_energy"]!.GetValue<int>(), gi[GI.MaxEnergy], where + " max_energy");
            Assert.Equal(obs["floor"]!.GetValue<int>(), gi[GI.TotalFloor], where + " floor");
            Assert.Equal(obs["act"]!.GetValue<int>(), gi[GI.ActIndex] + 1, where + " act");
            var combatNode = obs["combat"];
            Assert.Equal(combatNode != null ? 1 : 0, gi[GI.IsCombat], where + " is_combat");
            if (combatNode != null)
            {
                combat++;
                Assert.Equal(player["energy"]!.GetValue<int>(), gi[GI.Energy], where + " energy");
                Assert.Equal(player["block"]!.GetValue<int>(), gi[GI.Block], where + " block");
                damageChecks += CompareCombat(nb, combatNode, gi, cardVocab, where);
            }
            compared++;
            return compared < 300;
        });
        Console.WriteLine($"     server parity: {compared} states ({combat} combat), {damageChecks} effective-damage values compared");
        Assert.True(compared >= 200, $"only {compared} states compared");
        Assert.True(combat >= 100, $"only {combat} combat states compared");
        Assert.True(damageChecks >= 200, $"only {damageChecks} effective-damage values compared");
    }

    private static int CompareCombat(NativeBuffers nb, JsonNode combatNode, Span<int> gi, VocabList cardVocab, string where)
    {
        int hc = Schema.HC;
        var hand = nb.All<int>(Buf.Hand);
        var handDmg = nb.All<float>(Buf.HandDmg);
        var enemies = nb.All<int>(Buf.Enemies);
        var intents = nb.All<int>(Buf.EnemyIntents);
        var handNodes = combatNode["hand"]!.AsArray();
        Assert.Equal(handNodes.Count, gi[GI.HandN], where + " hand_n");
        // uid "uN" → enemy slot
        var slotOfUid = new Dictionary<string, int>(StringComparer.Ordinal);
        var enemyNodes = combatNode["enemies"]!.AsArray();
        Assert.Equal(enemyNodes.Count, gi[GI.EnemyN], where + " enemy_n");
        foreach (var e in enemyNodes)
        {
            int j = e!["index"]!.GetValue<int>();
            var row = enemies.Slice(j * 9, 9);
            Assert.Equal(1, row[0], where + $" enemy {j} present");
            Assert.Equal(e["uid"]!.GetValue<string>(), "u" + row[1], where + $" enemy {j} uid");
            Assert.Equal(e["id"]!.GetValue<string>(), Vocab.Monsters.NameOf(row[2]), where + $" enemy {j} id");
            Assert.Equal(e["hp"]!.GetValue<int>(), row[3], where + $" enemy {j} hp");
            Assert.Equal(e["max_hp"]!.GetValue<int>(), row[4], where + $" enemy {j} max_hp");
            Assert.Equal(e["block"]!.GetValue<int>(), row[5], where + $" enemy {j} block");
            var its = e["intents"]!.AsArray();
            Assert.Equal(Math.Min(its.Count, Caps.EnemyIntents), row[8], where + $" enemy {j} intent_n");
            for (int k = 0; k < row[8]; k++)
            {
                var it = intents.Slice((j * Caps.EnemyIntents + k) * 4, 4);
                Assert.Equal(its[k]!["type"]!.GetValue<string>(), ((IntentType)it[0]).ToString(), where + $" enemy {j} intent {k} type");
                int damage = its[k]!["damage"]?.GetValue<int>() ?? 0;
                Assert.Equal(damage, it[1], where + $" enemy {j} intent {k} damage");
                if (its[k]!["damage"] != null)
                {
                    Assert.Equal(its[k]!["times"]!.GetValue<int>(), it[2], where + $" enemy {j} intent {k} times");
                }
                Assert.Equal(its[k]!["card_count"]?.GetValue<int>() ?? 0, it[3], where + $" enemy {j} intent {k} card_count");
            }
            slotOfUid[e["uid"]!.GetValue<string>()] = j;
        }
        if (combatNode["danger"] is JsonObject danger)
        {
            Assert.Equal(danger["incoming"]!.GetValue<int>(), gi[GI.IncomingDamage], where + " incoming_damage");
            Assert.Equal(danger["hits"]!.GetValue<int>(), gi[GI.IncomingHits], where + " incoming_hits");
        }
        int damageChecks = 0;
        for (int i = 0; i < handNodes.Count; i++)
        {
            var c = handNodes[i]!;
            var row = hand.Slice(i * hc, hc);
            Assert.Equal(c["id"]!.GetValue<string>(), cardVocab.NameOf(row[HC.CardId]), where + $" hand {i} id");
            Assert.Equal(c["upgraded"]!.GetValue<bool>(), row[HC.Upgrade] > 0, where + $" hand {i} upgraded");
            var cost = c["cost"]!;
            if (cost.GetValueKind() == System.Text.Json.JsonValueKind.String)
            {
                Assert.Equal(HC.CostX, row[HC.CostCurrent], where + $" hand {i} X cost");
            }
            else
            {
                int expected = cost.GetValue<int>();
                int actual = row[HC.CostCurrent] == HC.CostUnplayable ? 0 : row[HC.CostCurrent];
                Assert.Equal(expected, actual, where + $" hand {i} cost");
            }
            Assert.Equal(c["playable"]!.GetValue<bool>(), row[HC.Playable] == 1, where + $" hand {i} playable");
            var dmg = handDmg.Slice(i * Caps.Enemies, Caps.Enemies);
            var effDamage = c["effective"]?["damage"] as JsonObject;
            foreach (var (uid, j) in slotOfUid)
            {
                float expected = effDamage != null && effDamage[uid] is { } v ? v.GetValue<int>() : 0;
                Assert.Equal(expected, dmg[j], where + $" hand {i} effective damage vs {uid}");
                damageChecks += effDamage != null ? 1 : 0;
            }
            if (c["effective"]?["times"] is { } times)
            {
                Assert.Equal(times.GetValue<int>(), row[HC.Hits], where + $" hand {i} hits");
            }
        }
        return damageChecks;
    }

    private static string VecSourceDir([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "..", "Sts2Sim.Vec");

    public static void Test_VecEncoder_NoMoveIds()
    {
        // H8: the encoder reads intents only. It must never touch the next move's identity or the AI's internal state.
        var files = Directory.GetFiles(VecSourceDir(), "ObservationEncoder*.cs");
        Assert.True(files.Length >= 2, "encoder sources found");
        string[] forbidden =
        {
            "NextMove.Id", "NextMove?.Id", "nextMove.Id", "nextMove?.Id", "MoveId", ".StateLog", "CurrentStateId",
            "PerformedAtLeastOnce", "PerformedFirstMove", "m.Extra", "MonsterExtraState", "EncounterExtra", "SeedText", ".Rng",
            "Odds", "GrabBag", "NextEventId", "NextNormalEncounter", "NextEliteEncounter", "InstanceId", "CurrentRelicIds",
        };
        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            foreach (var f in forbidden)
            {
                Assert.False(text.Contains(f, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains '{f}'");
            }
        }
        // Behavioural check: no intent row carries anything but (type, damage, times, card_count) of the intents.
        using var nb = new NativeBuffers(1);
        int checkedEnemies = 0;
        Playouts(10, 600, 41, heal: true, sim =>
        {
            if (sim.State.Combat is not { IsInProgress: true } cs)
            {
                return true;
            }
            EncodeInto(sim, nb, out _);
            var intents = nb.All<int>(Buf.EnemyIntents);
            for (int j = 0; j < Math.Min(cs.Monsters.Count, Caps.Enemies); j++)
            {
                var m = cs.Monsters[j];
                int n = m.IsEnemy && m.IsAlive && m.HasAi && m.Ai.NextMove != null ? Math.Min(m.Ai.NextMove.Intents.Count, Caps.EnemyIntents) : 0;
                for (int k = 0; k < Caps.EnemyIntents; k++)
                {
                    var it = intents.Slice((j * Caps.EnemyIntents + k) * 4, 4);
                    if (k >= n)
                    {
                        Assert.True(it.SequenceEqual(new int[4]), "padding intent rows are zero");
                        continue;
                    }
                    Assert.Equal((int)m.Ai.NextMove!.Intents[k].IntentType, it[0]);
                }
                checkedEnemies++;
            }
            return checkedEnemies < 2000;
        });
        Assert.True(checkedEnemies > 100, $"only {checkedEnemies} enemy slots checked");
    }

    public static void Test_VecEncoder_TreasureAndCardRewardHidden()
    {
        using var nb = new NativeBuffers(1);
        int treasureClosed = 0;
        int treasureOpened = 0;
        int rewardsHidden = 0;
        int cardRewardShown = 0;
        int hc = Schema.HC;
        Playouts(150, 2500, 53, heal: true, sim =>
        {
            if (sim.HasPendingChoice)
            {
                return true;
            }
            var rs = sim.State;
            if (sim.Phase == RunPhase.Treasure && RoomStack.CurrentRoom(rs) is TreasureRoom room)
            {
                EncodeInto(sim, nb, out _);
                var gi = nb.All<int>(Buf.GlobalI);
                if (!room.ChestOpened)
                {
                    Assert.Equal(0, gi[GI.TreasureOpened]);
                    Assert.Equal(0, gi[GI.TreasureRelicId], "H6: no treasure relic before the chest is opened");
                    if (room.CurrentRelicId != null)
                    {
                        treasureClosed++;
                        var open = sim.Clone();
                        open.Step(RunAction.Of(RunActionKind.OpenChest));
                        EncodeInto(open, nb, out _);
                        var openRoom = (TreasureRoom)RoomStack.CurrentRoom(open.State)!;
                        if (!open.HasPendingChoice && openRoom.ChestOpened && openRoom.CurrentRelicId != null)
                        {
                            Assert.Equal(1, nb.All<int>(Buf.GlobalI)[GI.TreasureOpened]);
                            Assert.Equal(Vocab.Relic(openRoom.CurrentRelicId), nb.All<int>(Buf.GlobalI)[GI.TreasureRelicId]);
                            treasureOpened++;
                        }
                    }
                }
            }
            if (sim.Phase == RunPhase.Rewards && RunLegalActionGenerator.TopRewards(rs) is { } set)
            {
                for (int i = 0; i < set.Rewards.Count && i < Caps.Rewards; i++)
                {
                    if (set.Rewards[i] is not CardReward cardReward || cardReward.SuccessfullySelected || cardReward.Cards.Count == 0)
                    {
                        continue;
                    }
                    EncodeInto(sim, nb, out _);
                    Assert.True(nb.All<int>(Buf.CardReward).ToArray().All(v => v == 0), "H7: no card-reward cards in the Rewards phase");
                    Assert.True(nb.All<int>(Buf.CardRewardAlts).ToArray().All(v => v == 0), "H7: no card-reward alternatives in the Rewards phase");
                    var row = nb.All<int>(Buf.Rewards).Slice(i * 4, 4);
                    Assert.Equal((int)RewardType.Card, row[0]);
                    Assert.Equal(1, row[3]);
                    Assert.Equal(0, row[2], "the card reward row names no card");
                    rewardsHidden++;
                    var claim = sim.Clone();
                    claim.Step(RunAction.Of(RunActionKind.ClaimReward, i));
                    if (claim.HasPendingChoice || claim.Phase != RunPhase.CardReward)
                    {
                        continue;
                    }
                    EncodeInto(claim, nb, out _);
                    var open = RunLegalActionGenerator.OpenCardReward(claim.State, claim.Loop)!;
                    var cr = nb.All<int>(Buf.CardReward);
                    for (int c = 0; c < Math.Min(open.Cards.Count, Caps.CardReward); c++)
                    {
                        Assert.Equal(Vocab.Card(open.Cards[c].Card.Id), cr[c * hc + HC.CardId], $"card reward card {c}");
                        Assert.Equal(open.Cards[c].Card.UpgradeLevel, cr[c * hc + HC.Upgrade]);
                    }
                    cardRewardShown++;
                    break;
                }
            }
            return treasureOpened < 5 || cardRewardShown < 20;
        }, (sim, legal) =>
        {
            // Steer the map walk into treasure rooms.
            if (sim.Phase != RunPhase.Map || sim.HasPendingChoice)
            {
                return -1;
            }
            var points = MapTravel.GetLegalMapPoints(sim.State);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Kind == RunActionKind.Travel && legal[i].A < points.Count && points[legal[i].A].PointType == MapPointType.Treasure)
                {
                    return i;
                }
            }
            return -1;
        });
        Console.WriteLine($"     hidden info: treasure closed {treasureClosed} / opened {treasureOpened}; card rewards hidden {rewardsHidden} / shown {cardRewardShown}");
        Assert.True(treasureOpened >= 3, $"only {treasureOpened} treasure chests checked");
        Assert.True(cardRewardShown >= 10, $"only {cardRewardShown} card rewards checked");
    }

    public static void Test_VecEncoder_TablesRespectCaps()
    {
        RunSim? sample = null;
        Playouts(5, 400, 61, heal: true, sim =>
        {
            if (!sim.HasPendingChoice && sim.Phase == RunPhase.Combat && sim.State.Combat is { IsInProgress: true } && sim.State.TotalFloor >= 2)
            {
                sample = sim.Clone();
                return false;
            }
            return true;
        });
        Assert.NotNull(sample, "a combat state to boost");
        var rs = sample!.State;
        var p = rs.Players[0];
        int start = p.Deck.Count;
        for (int i = 0; p.Deck.Count < 110; i++)
        {
            p.Deck.Add(rs.CloneCard(p.Deck[i % start]));
        }
        using var nb = new NativeBuffers(1);
        EncodeInto(sample, nb, out var st);
        Assert.True(st.HasFlag(SlotStatus.TableOverflow), "a 110-card deck overflows CAP_DECK");
        var gi = nb.All<int>(Buf.GlobalI);
        Assert.Equal(110, gi[GI.DeckN], "deck_n is the true count");
        var deck = nb.All<int>(Buf.Deck);
        int rows = 0;
        for (int r = 0; r < Caps.Deck; r++)
        {
            if (deck[r * 5] != 0)
            {
                rows++;
            }
            Assert.Equal(Vocab.Card(p.Deck[r].Id), deck[r * 5], $"deck row {r} in pickup order");
        }
        Assert.Equal(Caps.Deck, rows, "exactly CAP_DECK rows written");

        // A state without boosted tables does not set the bit; counts never exceed the piles.
        var plain = sample.Clone();
        var pd = plain.State.Players[0].Deck;
        pd.RemoveRange(start, pd.Count - start);
        EncodeInto(plain, nb, out var st2);
        Assert.False(st2.HasFlag(SlotStatus.TableOverflow), "no overflow on an ordinary state");
        var cp = plain.State.Combat!.Player;
        int drawTotal = 0;
        foreach (var v in nb.All<short>(Buf.DrawCounts))
        {
            drawTotal += v;
        }
        Assert.Equal(cp.DrawPile.Count, drawTotal, "draw counts sum to the pile size");
    }
    /// <summary>
    /// The protocol-v0.2 progress columns appended to <c>aux</c> (act_floor, map_row, map_col, act_rooms): equal to the
    /// header columns, act_rooms = the map table's last row + 1 (the play server's <c>map.nodes</c> max row + 1), and
    /// total_floor - act_floor = the act's first observed total floor (the protocol's act start floor).
    /// </summary>
    public static void Test_VecEncoder_AuxProgressColumns()
    {
        using var nb = new NativeBuffers(1);
        int states = 0, mapped = 0, actsSeen = 0, startMismatch = 0;
        int lastAct = -1, lastTotal = int.MaxValue, actStart = 0;
        var seenRooms = new SortedSet<int>();
        Playouts(10, 1500, 83, heal: true, sim =>
        {
            EncodeInto(sim, nb, out _);
            var gi = nb.All<int>(Buf.GlobalI);
            var aux = nb.All<float>(Buf.Aux);
            states++;
            Assert.Equal(gi[GI.ActFloor], (int)aux[AuxCol.ActFloor], "aux.act_floor");
            Assert.Equal(gi[GI.MapRow], (int)aux[AuxCol.MapRow], "aux.map_row");
            Assert.Equal(gi[GI.MapCol], (int)aux[AuxCol.MapCol], "aux.map_col");
            var nodes = nb.All<int>(Buf.MapNodes);
            int maxRow = -1;
            for (int r = 0; r < Caps.MapRows; r++)
            {
                for (int c = 0; c < Caps.MapCols; c++)
                {
                    if (nodes[(r * Caps.MapCols + c) * 6] != 0)
                    {
                        maxRow = r;
                    }
                }
            }
            int rooms = (int)aux[AuxCol.ActRooms];
            if (sim.State.Map != null)
            {
                mapped++;
                Assert.Equal(maxRow + 1, rooms, "aux.act_rooms = map_nodes last row + 1");
                seenRooms.Add(rooms);
            }
            int act = gi[GI.ActIndex];
            int total = gi[GI.TotalFloor];
            if (act != lastAct || total < lastTotal)
            {
                // Protocol v0.2: act 1 starts at floor 0; a later act at the floor of its first observation.
                actStart = act == 0 ? 0 : total;
                actsSeen++;
            }
            lastAct = act;
            lastTotal = total;
            if (total - gi[GI.ActFloor] != actStart)
            {
                startMismatch++;
            }
            return true;
        });
        Console.WriteLine($"     aux progress columns: {states} states, {mapped} with a map, {actsSeen} act starts, act_rooms seen {{{string.Join(",", seenRooms)}}}, start mismatches {startMismatch}");
        Assert.True(actsSeen >= 15, $"only {actsSeen} act starts");
        Assert.Equal(0, startMismatch, "total_floor - act_floor = the protocol's act start floor");
        Assert.True(seenRooms.IsSubsetOf(new[] { 15, 16, 17, 18 }), "act room counts are 15..18");
    }
}
