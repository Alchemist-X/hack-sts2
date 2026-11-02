using System.Runtime.CompilerServices;
using System.Text;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>V2 env-core tests (CONTRACTS.md §D7 T1, T3-T8): VecEnv stepping, candidate table, start pool, threads, ABI.</summary>
public static unsafe partial class VecTests
{
    private static readonly string[] AllCharacters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    private static string SaveFixturePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", "run_save_a8_silent_boss1.save");

    private static VecEnv NewEnv(int n, int threads, int poolCount = 8, int stepCap = 3000, bool autoReset = true,
        string seedText = "VEC", string content = "ported_only", int ascension = 0)
        => new(new VecConfig(n, Threads: threads, StepCap: stepCap, AutoReset: autoReset, SeedText: seedText, Content: content,
            Pool: poolCount > 0 ? new FreshPoolSpec(AllCharacters, Ascension: ascension, Count: poolCount, SeedPrefix: "VT") : null));

    private static ulong[] SlotRngs(int n, ulong salt = 0)
    {
        var r = new ulong[n];
        for (int i = 0; i < n; i++)
        {
            r[i] = 0x9E3779B97F4A7C15UL * (ulong)(i + 1) ^ (salt * 0xD1B54A32D192ED03UL) | 1UL;
        }
        return r;
    }

    private static int NextIndex(ref ulong x, int n)
    {
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        return (int)(x % (ulong)n);
    }

    private static double NextUnit(ref ulong x) => NextIndex(ref x, 1 << 24) / (double)(1 << 24);

    /// <summary>Uniform-random candidate per slot (−1 when the slot lists none).</summary>
    private static void RandomActions(NativeBuffers nb, ulong[] rng, int[] actions)
    {
        var candN = nb.All<int>(Buf.CandN);
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i] = candN[i] > 0 ? NextIndex(ref rng[i], candN[i]) : -1;
        }
    }

    private static Span<int> CandRow(NativeBuffers nb, int slot, int row)
    {
        int w = Schema.CandFields.Length;
        return nb.Slot(slot).Cand.Slice(row * w, w);
    }

    /// <summary>A play-biased policy over the candidate kinds (the soak's "choose", driven only by the cand table).</summary>
    private static int PlayBiased(NativeBuffers nb, int slot, ref ulong rng, bool endTurnOnly = false)
    {
        var box = new[] { rng };
        int pick = PlayBiased(nb, slot, box, endTurnOnly);
        rng = box[0];
        return pick;
    }

    private static int PlayBiased(NativeBuffers nb, int slot, ulong[] box, bool endTurnOnly)
    {
        int n = nb.All<int>(Buf.CandN)[slot];
        if (n <= 0)
        {
            return -1;
        }
        var byKind = new Dictionary<RunActionKind, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var k = (RunActionKind)CandRow(nb, slot, i)[CandCol.Kind];
            if (!byKind.TryGetValue(k, out var l))
            {
                byKind[k] = l = new List<int>();
            }
            l.Add(i);
        }
        int Pick(RunActionKind k) => byKind[k][NextIndex(ref box[0], byKind[k].Count)];
        bool Has(RunActionKind k) => byKind.ContainsKey(k);
        if (Has(RunActionKind.EndTurn))
        {
            if (endTurnOnly)
            {
                return Pick(RunActionKind.EndTurn);
            }
            if (Has(RunActionKind.PlayCard) && NextUnit(ref box[0]) < 0.85) return Pick(RunActionKind.PlayCard);
            if (Has(RunActionKind.UsePotion) && NextUnit(ref box[0]) < 0.15) return Pick(RunActionKind.UsePotion);
            return Pick(RunActionKind.EndTurn);
        }
        if (Has(RunActionKind.ClaimReward)) return NextUnit(ref box[0]) < 0.9 || !Has(RunActionKind.ProceedRewards) ? Pick(RunActionKind.ClaimReward) : Pick(RunActionKind.ProceedRewards);
        if (Has(RunActionKind.ProceedRewards)) return Pick(RunActionKind.ProceedRewards);
        if (Has(RunActionKind.PickRewardCard) && NextUnit(ref box[0]) < 0.85) return Pick(RunActionKind.PickRewardCard);
        if (Has(RunActionKind.BuyEntry) && NextUnit(ref box[0]) < 0.6) return Pick(RunActionKind.BuyEntry);
        if (Has(RunActionKind.LeaveShop)) return Pick(RunActionKind.LeaveShop);
        if (Has(RunActionKind.OpenChest)) return Pick(RunActionKind.OpenChest);
        if (Has(RunActionKind.TakeTreasureRelic)) return Pick(RunActionKind.TakeTreasureRelic);
        if (Has(RunActionKind.ChooseEventOption)) return Pick(RunActionKind.ChooseEventOption);
        if (Has(RunActionKind.Travel)) return Pick(RunActionKind.Travel);
        // prompts, rest sites, card-reward alternatives, leave-event: uniform, but never a bare potion discard if avoidable
        for (int tries = 0; tries < 8; tries++)
        {
            int i = NextIndex(ref box[0], n);
            if ((RunActionKind)CandRow(nb, slot, i)[CandCol.Kind] != RunActionKind.DiscardPotion)
            {
                return i;
            }
        }
        return NextIndex(ref box[0], n);
    }

    private static void AssertBuffersEqual(NativeBuffers x, NativeBuffers y, string what, int skip = -1)
    {
        for (int k = 0; k < Buf.Count; k++)
        {
            if (k == skip)
            {
                continue;
            }
            if (!x.AllBytes(k).SequenceEqual(y.AllBytes(k)))
            {
                var a = x.AllBytes(k);
                var b = y.AllBytes(k);
                int at = 0;
                while (at < a.Length && a[at] == b[at]) at++;
                Assert.Fail($"{what}: buffer {Schema.Buffers[k].Name} differs (first byte {at}, slot {at / Schema.BytesPerEnv(k)})");
            }
        }
    }

    private static ulong[] Hashes(VecEnv env) => Enumerable.Range(0, env.NumEnvs).Select(env.StateHash).ToArray();

    /// <summary>Steps the sim's first legal action until no prompt is pending (state edits on a pending sim are lost on resume).</summary>
    private static void ClearPending(RunSim sim)
    {
        for (int g = 0; g < 50 && sim.HasPendingChoice; g++)
        {
            sim.Step(sim.LegalActions()[0]);
        }
        Assert.False(sim.HasPendingChoice, "could not leave the opening prompt");
    }

    private static RunSim BoostedStart(string character, string seed, int hp)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, character, 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var p = sim.State.Players[0];
        p.MaxHp = hp;
        p.CurrentHp = hp;
        return sim;
    }

    /// <summary>
    /// A run whose deck holds 250 WISH copies (0-cost "choose a card from your draw pile"), driven into the first combat
    /// until a WISH is played: the pending PileSelect prompt lists every draw-pile card (> CAP_CANDIDATES).
    /// </summary>
    private static RunSim WishPromptSim()
    {
        var sim = RunSim.NewRun(new RunConfig("VEC-OVERFLOW", "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var rs = sim.State;
        var p = rs.Players[0];
        for (int i = 0; i < 250; i++)
        {
            var card = new CardInstance(CardRegistry.Get(Wish.CardId), rs.AllocateCardInstanceId());
            card.AttachTo(p);
            p.Deck.Add(card);
        }
        p.MaxHp = 9999;
        p.CurrentHp = 9999;
        for (int step = 0; step < 3000 && !sim.IsTerminal; step++)
        {
            var legal = sim.LegalActions().ToList();
            if (sim.HasPendingChoice && legal.Count > Caps.Candidates)
            {
                return sim;
            }
            RunAction pick = legal[0];
            if (!sim.HasPendingChoice && sim.State.Combat is { IsInProgress: true } cs)
            {
                int w = legal.FindIndex(a => a.Kind == RunActionKind.PlayCard && cs.Player.Hand[a.A].Id == Wish.CardId);
                pick = w >= 0 ? legal[w] : legal[legal.FindIndex(a => a.Kind == RunActionKind.EndTurn)];
            }
            sim.Step(pick);
        }
        Assert.Fail("no WISH prompt with more than CAP_CANDIDATES options was reached");
        return sim;
    }

    // ------------------------------------------------------------------------------------------- T1 / T7 determinism

    public static void Test_VecEnv_DeterministicAcrossThreadsAndInstances()
    {
        const int N = 256, Steps = 300;
        var envs = new[] { NewEnv(N, 1, 16), NewEnv(N, 8, 16), NewEnv(N, 8, 16) };
        var bufs = envs.Select(_ => new NativeBuffers(N)).ToArray();
        try
        {
            foreach (var (e, b) in envs.Zip(bufs))
            {
                e.Reset(default, default, b.Table);
            }
            AssertBuffersEqual(bufs[0], bufs[1], "reset 1 vs 8 threads");
            AssertBuffersEqual(bufs[0], bufs[2], "reset instance A vs B");
            var rng = SlotRngs(N);
            var actions = new int[N];
            long decisions = 0, resets = 0;
            for (int t = 0; t < Steps; t++)
            {
                RandomActions(bufs[0], rng, actions);
                decisions += actions.Count(a => a >= 0);
                foreach (var (e, b) in envs.Zip(bufs))
                {
                    e.Step(actions, b.Table);
                }
                AssertBuffersEqual(bufs[0], bufs[1], $"step {t}, 1 vs 8 threads");
                AssertBuffersEqual(bufs[0], bufs[2], $"step {t}, instance A vs B");
                var st = bufs[0].All<int>(Buf.Status);
                for (int i = 0; i < N; i++)
                {
                    Assert.Equal(0, st[i] & (int)(SlotStatus.SimError | SlotStatus.ContentGap | SlotStatus.IllegalAction), $"slot {i} step {t}: {envs[0].Slots[i].LastError}");
                    if ((st[i] & (int)SlotStatus.Reset) != 0) resets++;
                }
            }
            var h0 = Hashes(envs[0]);
            Assert.SequenceEqual(h0, Hashes(envs[1]), "StateHash 1 vs 8 threads");
            Assert.SequenceEqual(h0, Hashes(envs[2]), "StateHash instance A vs B");
            Assert.True(h0.All(h => h != 0), "every slot holds a run");
            Assert.True(decisions > N * (Steps - 1), $"decisions {decisions}");
            Assert.True(resets > 0, "some episodes ended and auto-reset within 300 random steps");
        }
        finally
        {
            foreach (var b in bufs) b.Dispose();
        }
    }

    public static void Test_VecEnv_ThreadsDoNotChangeHashes()
    {
        const int N = 1024, Steps = 200;
        var a = NewEnv(N, 1, 16, seedText: "T7");
        var b = NewEnv(N, 12, 16, seedText: "T7");
        using var na = new NativeBuffers(N);
        using var nb = new NativeBuffers(N);
        a.Reset(default, default, na.Table);
        b.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 7);
        var actions = new int[N];
        for (int t = 0; t < Steps; t++)
        {
            RandomActions(na, rng, actions);
            a.Step(actions, na.Table);
            b.Step(actions, nb.Table);
        }
        Assert.SequenceEqual(Hashes(a), Hashes(b), "StateHash 1 vs 12 threads");
        AssertBuffersEqual(na, nb, "buffers after 200 steps, 1 vs 12 threads");
    }

    // ------------------------------------------------------------------------------------------- T3 overflow

    public static void Test_VecEnv_OverflowStatus()
    {
        var prompt = WishPromptSim();
        var legal = prompt.LegalActions().ToList();
        Assert.True(legal.Count > Caps.Candidates, $"legal {legal.Count}");

        var env = NewEnv(2, 2, 0);
        int entry = env.Pool.AddSnapshot(prompt.Clone());
        using var nb = new NativeBuffers(2);
        env.Restore(0, entry, nb.Table);
        var s = nb.Slot(0);
        Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.CandidateOverflow), $"status {s.Status}");
        Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset));
        Assert.Equal(Caps.Candidates, s.CandN);
        for (int i = 0; i < Caps.Candidates; i++)
        {
            Assert.Equal(legal[i], CandidateTable.ToAction(CandRow(nb, 0, i)), $"row {i}");
            Assert.Equal(RunActionKind.PromptSelect, legal[i].Kind);
            Assert.Equal(legal[i].A, CandRow(nb, 0, i)[CandCol.OptionIndex]);
            Assert.Equal(Vocab.Card(prompt.PendingChoice!.Options[legal[i].A].ModelId), CandRow(nb, 0, i)[CandCol.CardId], $"row {i} card_id");
        }
        Assert.Equal(prompt.StateHash(), env.StateHash(0));

        // the last listed candidate is selectable and resolves the prompt like the RunAction does
        var expect = prompt.Clone();
        expect.Step(legal[Caps.Candidates - 1]);
        var actions = new[] { Caps.Candidates - 1, -1 };
        env.Step(actions, nb.Table);
        Assert.Equal(0, s.Status & (int)(SlotStatus.IllegalAction | SlotStatus.SimError), $"status {s.Status}");
        Assert.Equal(expect.StateHash(), env.StateHash(0));
        Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.CandidateOverflow), "the combat after the pick lists < 192 actions");

        // direct Fill: rows beyond the cap are not written, and a short list zero-fills the rest of the table
        var cand = new int[Caps.Candidates * Schema.CandFields.Length];
        Array.Fill(cand, 77);
        int n = CandidateTable.Fill(prompt, legal, cand, out bool overflow);
        Assert.True(overflow);
        Assert.Equal(Caps.Candidates, n);
        Array.Fill(cand, 77);
        n = CandidateTable.Fill(prompt, legal.Take(3).ToList(), cand, out overflow);
        Assert.False(overflow);
        Assert.Equal(3, n);
        Assert.True(cand.Skip(3 * Schema.CandFields.Length).All(v => v == 0), "rows at or beyond cand_n are zero");
    }

    // ------------------------------------------------------------------------------------------- T4 candidates

    public static void Test_VecCand_MatchesLegalActions()
    {
        const int N = 64;
        var env = NewEnv(N, 8, 16, seedText: "T4");
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 4);
        var actions = new int[N];
        // Shadow runs: a second materialisation of each slot's start entry, stepped by RunAction alongside the env (clone-then-
        // step fidelity itself is SelfConsistencyTests.Test_SelfConsistency_F7_CloneThenStepMatchesStep).
        var shadow = new RunSim?[N];
        var shadowEpisode = new long[N];
        var stepped = new bool[N];
        var kinds = new HashSet<RunActionKind>();
        int checkedPoints = 0, steppedCompared = 0;
        for (int t = 0; checkedPoints < 10_000; t++)
        {
            Assert.True(t < 2_000, "too many steps without reaching 10,000 decision points");
            RandomActions(nb, rng, actions);
            for (int i = 0; i < N; i++)
            {
                var sim = env.Slots[i].Sim;
                stepped[i] = false;
                if (sim == null)
                {
                    continue;
                }
                if (shadow[i] == null || shadowEpisode[i] != env.Slots[i].EpisodeId)
                {
                    shadow[i] = env.Pool.Materialize(env.Slots[i].StartEntry);
                    shadowEpisode[i] = env.Slots[i].EpisodeId;
                    Assert.Equal(shadow[i]!.StateHash(), env.StateHash(i), $"slot {i}: fresh episode = its start entry");
                }
                var legal = sim.LegalActions().ToList();
                int candN = nb.All<int>(Buf.CandN)[i];
                Assert.Equal(Math.Min(legal.Count, Caps.Candidates), candN, $"slot {i} cand_n");
                Assert.Equal(legal.Count > Caps.Candidates, ((SlotStatus)nb.All<int>(Buf.Status)[i]).HasFlag(SlotStatus.CandidateOverflow));
                var cs = sim.State.Combat;
                for (int r = 0; r < candN; r++)
                {
                    var row = CandRow(nb, i, r);
                    var a = legal[r];
                    kinds.Add(a.Kind);
                    Assert.Equal((int)a.Kind, row[CandCol.Kind]);
                    Assert.Equal(a.A, row[CandCol.A]);
                    Assert.Equal(a.B, row[CandCol.B]);
                    Assert.Equal(a, CandidateTable.ToAction(row));
                    Assert.True(sim.IsLegal(a), $"candidate {a} is legal");
                    bool play = a.Kind == RunActionKind.PlayCard;
                    bool potion = a.Kind is RunActionKind.UsePotion or RunActionKind.DiscardPotion;
                    Assert.Equal(play ? a.A : -1, row[CandCol.HandSlot], $"{a} hand_slot");
                    Assert.Equal(potion ? a.A : -1, row[CandCol.PotionSlot], $"{a} potion_slot");
                    bool targeted = a.Kind is RunActionKind.PlayCard or RunActionKind.UsePotion && a.B >= 0;
                    Assert.Equal(targeted ? a.B : -1, row[CandCol.TargetSlot], $"{a} target_slot");
                    Assert.Equal(targeted ? cs!.Monsters[a.B].CombatId : 0, row[CandCol.TargetUid], $"{a} target_uid");
                    bool option = a.Kind is RunActionKind.Travel or RunActionKind.ClaimReward or RunActionKind.PickRewardCard
                        or RunActionKind.RewardAlternative or RunActionKind.BuyEntry or RunActionKind.ChooseRestOption
                        or RunActionKind.ChooseEventOption or RunActionKind.PromptSelect;
                    Assert.Equal(option ? a.A : -1, row[CandCol.OptionIndex], $"{a} option_index");
                    if (play)
                    {
                        Assert.Equal(Vocab.Card(cs!.Player.Hand[a.A].Id), row[CandCol.CardId], $"{a} card_id");
                        Assert.True(row[CandCol.CardId] >= 2, $"{a} card_id is a real id");
                    }
                    else if (a.Kind == RunActionKind.PickRewardCard)
                    {
                        Assert.True(row[CandCol.CardId] >= 2, $"{a} card_id");
                    }
                    else if (a.Kind != RunActionKind.PromptSelect)
                    {
                        Assert.Equal(0, row[CandCol.CardId], $"{a} card_id");
                    }
                }
                Assert.True(nb.Slot(i).Cand[(candN * Schema.CandFields.Length)..].ToArray().All(v => v == 0), $"slot {i} padding rows");
                checkedPoints++;
                if (actions[i] >= 0)
                {
                    shadow[i]!.Step(legal[actions[i]]);
                    stepped[i] = true;
                }
            }
            env.Step(actions, nb.Table);
            for (int i = 0; i < N; i++)
            {
                if (stepped[i] && env.Slots[i].EpisodeId == shadowEpisode[i])
                {
                    Assert.Equal(shadow[i]!.StateHash(), env.StateHash(i), $"slot {i} step {t}: candidate index ≡ RunAction");
                    steppedCompared++;
                }
            }
        }
        Assert.True(steppedCompared > 9_000, $"compared {steppedCompared}");
        foreach (var k in new[] { RunActionKind.PlayCard, RunActionKind.EndTurn, RunActionKind.Travel })
        {
            Assert.True(kinds.Contains(k), $"saw {k}");
        }
    }

    public static void Test_VecCand_IllegalIndexLeavesStateUnchanged()
    {
        const int N = 4;
        var env = NewEnv(N, 2, 4, seedText: "ILL");
        using var nb = new NativeBuffers(N);
        using var before = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 9);
        var actions = new int[N];
        for (int t = 0; t < 25; t++)
        {
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
        }
        // a clean no-op step so reward/terminated/truncated are 0 in the reference copy
        env.Step(new[] { -1, -1, -1, -1 }, nb.Table);
        var hashes = Hashes(env);
        for (int k = 0; k < Buf.Count; k++)
        {
            nb.AllBytes(k).CopyTo(before.AllBytes(k));
        }
        var candN = nb.All<int>(Buf.CandN);
        var illegal = new[] { candN[0], Caps.Candidates, int.MaxValue, candN[3] + 5 };
        env.Step(illegal, nb.Table);
        var st = nb.All<int>(Buf.Status);
        for (int i = 0; i < N; i++)
        {
            Assert.True(((SlotStatus)st[i]).HasFlag(SlotStatus.IllegalAction), $"slot {i} status {st[i]}");
            Assert.False(((SlotStatus)st[i]).HasFlag(SlotStatus.Reset), $"slot {i} not reset");
        }
        Assert.SequenceEqual(hashes, Hashes(env), "illegal indices leave every slot's state unchanged");
        AssertBuffersEqual(before, nb, "illegal step rewrites the same observation", skip: Buf.Status);
        Assert.True(nb.All<float>(Buf.Reward).ToArray().All(r => r == 0f));
        Assert.True(nb.All<byte>(Buf.Terminated).ToArray().All(r => r == 0));

        // a legal step afterwards works
        RandomActions(nb, rng, actions);
        env.Step(actions, nb.Table);
        Assert.True(st.ToArray().All(v => (v & (int)SlotStatus.IllegalAction) == 0));
    }

    // ------------------------------------------------------------------------------------------- T5 episodes

    public static void Test_VecEnv_RewardOnlyOnVictory()
    {
        // Victory: a 99,999-HP run driven by a play-biased policy over the candidate table.
        bool won = false;
        foreach (var seed in new[] { "VEC-WIN-1", "VEC-WIN-2", "VEC-WIN-3" })
        {
            var env = new VecEnv(new VecConfig(1, Threads: 1, StepCap: 9000));
            int entry = env.Pool.AddSnapshot(BoostedStart("IRONCLAD", seed, 99_999));
            using var nb = new NativeBuffers(1);
            env.Restore(0, entry, nb.Table);
            var s = nb.Slot(0);
            Assert.Equal(1L, s.Episode[EpisodeCol.EpisodeId]);
            ulong rng = 12345;
            var act = new int[1];
            for (int t = 0; t < 9000; t++)
            {
                act[0] = PlayBiased(nb, 0, ref rng);
                Assert.True(act[0] >= 0, $"step {t}: no candidates");
                env.Step(act, nb.Table);
                Assert.Equal(0, s.Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap), env.Slots[0].LastError ?? "");
                if (s.Terminated == 0 && s.Truncated == 0)
                {
                    Assert.Equal(0f, s.Reward, $"step {t}: reward 0 before the end");
                    continue;
                }
                var le = s.LastEpisode;
                Assert.Equal(1, (int)s.Terminated);
                Assert.Equal((int)EndKind.Terminal, le[LastEpCol.EndKind]);
                Assert.Equal(t + 1, le[LastEpCol.Steps]);
                Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset), "auto-reset in the same step");
                Assert.Equal(2L, s.Episode[EpisodeCol.EpisodeId], "episode_id bumps (N = 1)");
                Assert.Equal(0L, s.Episode[EpisodeCol.StepIndex]);
                Assert.True(s.CandN > 0, "the new episode's first candidates are written");
                if (le[LastEpCol.Win] == 1)
                {
                    Assert.Equal(1f, s.Reward, "reward 1 at Victory");
                    Assert.True(le[LastEpCol.Floor] >= 48, $"victory floor {le[LastEpCol.Floor]}");
                    won = true;
                }
                else
                {
                    Assert.Equal(0f, s.Reward, "reward 0 at Defeat");
                }
                break;
            }
            if (won)
            {
                break;
            }
        }
        Assert.True(won, "a 99,999-HP play-biased run reaches Victory in one of three seeds");

        // Defeat: 1 HP and a policy that only ends turns.
        {
            var env = new VecEnv(new VecConfig(1, Threads: 1));
            int entry = env.Pool.AddSnapshot(BoostedStart("SILENT", "VEC-LOSE", 1));
            using var nb = new NativeBuffers(1);
            env.Restore(0, entry, nb.Table);
            var s = nb.Slot(0);
            ulong rng = 99;
            var act = new int[1];
            bool lost = false;
            for (int t = 0; t < 3000 && !lost; t++)
            {
                act[0] = PlayBiased(nb, 0, ref rng, endTurnOnly: true);
                env.Step(act, nb.Table);
                if (s.Terminated != 0)
                {
                    Assert.Equal(0f, s.Reward, "Defeat pays 0");
                    Assert.Equal(0, s.LastEpisode[LastEpCol.Win]);
                    Assert.Equal((int)EndKind.Terminal, s.LastEpisode[LastEpCol.EndKind]);
                    Assert.True(s.LastEpisode[LastEpCol.LastEncounterId] >= 2, "last_encounter_id names the fight");
                    lost = true;
                }
                else
                {
                    Assert.Equal(0f, s.Reward);
                }
            }
            Assert.True(lost, "a 1-HP end-turn-only run is defeated");
        }

        // Truncation at step_cap, with and without auto-reset.
        foreach (bool auto in new[] { true, false })
        {
            var env = NewEnv(2, 1, 2, stepCap: 5, autoReset: auto, seedText: "CAP");
            using var nb = new NativeBuffers(2);
            env.Reset(default, default, nb.Table);
            var rng = SlotRngs(2, 5);
            var actions = new int[2];
            var ep0 = nb.All<long>(Buf.Episode).ToArray();
            for (int t = 0; t < 5; t++)
            {
                RandomActions(nb, rng, actions);
                Assert.True(actions.All(a => a >= 0));
                env.Step(actions, nb.Table);
                if (t < 4)
                {
                    Assert.True(nb.All<byte>(Buf.Truncated).ToArray().All(v => v == 0), $"step {t}");
                }
            }
            for (int i = 0; i < 2; i++)
            {
                var s = nb.Slot(i);
                if (s.LastEpisode[LastEpCol.EndKind] == (int)EndKind.Terminal)
                {
                    continue; // (cannot happen in 5 decisions; kept defensive)
                }
                Assert.Equal(1, (int)s.Truncated, $"slot {i} truncated");
                Assert.Equal(0, (int)s.Terminated);
                Assert.Equal(0f, s.Reward);
                Assert.Equal((int)EndKind.Truncated, s.LastEpisode[LastEpCol.EndKind]);
                Assert.Equal(5, s.LastEpisode[LastEpCol.Steps]);
                if (auto)
                {
                    Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset));
                    Assert.Equal(ep0[i * 3 + EpisodeCol.EpisodeId] + 2, s.Episode[EpisodeCol.EpisodeId], "episode_id += N");
                    Assert.Equal(0L, s.Episode[EpisodeCol.StepIndex]);
                    Assert.True(s.CandN > 0);
                }
                else
                {
                    Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset));
                    Assert.Equal(0, s.CandN, "a finished slot lists no candidates");
                    Assert.Equal(ep0[i * 3 + EpisodeCol.EpisodeId], s.Episode[EpisodeCol.EpisodeId]);
                }
            }
            if (!auto)
            {
                env.Step(new[] { 0, -1 }, nb.Table);
                Assert.True(((SlotStatus)nb.Slot(0).Status).HasFlag(SlotStatus.IllegalAction), "a finished slot rejects actions");
                Assert.True(((SlotStatus)nb.Slot(1).Status).HasFlag(SlotStatus.Noop));
                Assert.Equal(0, nb.Slot(0).CandN);
                env.Reset(default, default, nb.Table);
                Assert.True(nb.Slot(0).CandN > 0, "reset restarts a finished slot");
                Assert.Equal(ep0[EpisodeCol.EpisodeId] + 2, nb.Slot(0).Episode[EpisodeCol.EpisodeId]);
            }
        }
    }

    // ------------------------------------------------------------------------------------------- T6 starts

    public static void Test_VecPool_SaveFixtureStartsAtRewards()
    {
        string path = SaveFixturePath();
        foreach (string content in new[] { "ported_only", "all" })
        {
            var env = NewEnv(2, 2, 0, content: content);
            var filter = VecConfig.ParseContent(content);
            var direct = RunImporter.FromSave(RunSaveReader.ReadFile(path), ImportOptions.Default with { Content = filter });
            Assert.True(direct.IsSupported, $"{content}: the fixture imports ({string.Join("; ", direct.UnsupportedReasons)})");
            Assert.False(direct.Sim!.HasPendingChoice);
            int entry = env.Pool.AddSave(path, env.Content);
            Assert.Equal(StartKind.Save, env.Pool[entry].Kind);
            using var nb = new NativeBuffers(2);
            env.Reset(default, new[] { entry, entry }, nb.Table);
            for (int i = 0; i < 2; i++)
            {
                var s = nb.Slot(i);
                Assert.Equal((int)RunPhase.Rewards, s.GlobalI[GI.Phase], $"{content}: phase");
                Assert.Equal(3, s.CandN, $"{content}: candidates at the fixture's rewards screen");
                Assert.Equal(direct.Sim!.StateHash(), env.StateHash(i), $"{content}: hash equals RunImporter.FromSave");
                Assert.Equal((long)entry, s.Episode[EpisodeCol.StartEntry]);
            }
            env.Step(new[] { 0, 0 }, nb.Table);
            Assert.Equal(env.StateHash(0), env.StateHash(1), "two slots from one save step identically");
            var c = direct.Sim!.Clone();
            c.Step(c.LegalActions()[0]);
            Assert.Equal(c.StateHash(), env.StateHash(0));
        }

        // through the export: entry id, then reasons on a bad path
        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        try
        {
            long h = CreateEnv("{\"num_envs\": 1, \"content\": \"all\"}");
            try
            {
                delegate* unmanaged<long, byte*, int, int> addSave = &Exports.PoolAddSave;
                var bytes = Encoding.UTF8.GetBytes(path);
                fixed (byte* p = bytes)
                {
                    Assert.Equal(0, addSave(h, p, bytes.Length), Exports.GlobalError);
                }
                Assert.Equal(1, Exports.EnvOf(h)!.Pool.Count);
            }
            finally
            {
                Destroy(h);
            }
        }
        finally
        {
            Exports.RequireServerGC = saved;
        }
    }

    public static void Test_VecPool_SnapshotRestoreRoundTrip()
    {
        const int N = 8;
        var env = NewEnv(N, 4, 8, seedText: "SNAP");
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 3);
        var actions = new int[N];
        for (int t = 0; t < 40; t++)
        {
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
        }
        // mid-run snapshot of slot 0 → restore into slot 1: equal hashes, equal observations, equal futures
        int before = env.Pool.Count;
        int snap = env.Snapshot(0);
        Assert.Equal(before, snap);
        Assert.Equal(StartKind.Snapshot, env.Pool[snap].Kind);
        ulong h0 = env.StateHash(0);
        env.Restore(1, snap, nb.Table);
        env.Restore(5, snap, nb.Table);
        Assert.Equal(h0, env.StateHash(1));
        Assert.Equal(h0, env.StateHash(5));
        Assert.True(nb.Slot(0).Cand.SequenceEqual(nb.Slot(1).Cand), "same candidates");
        Assert.True(nb.Slot(0).GlobalI.SequenceEqual(nb.Slot(1).GlobalI), "same observation");
        Assert.True(((SlotStatus)nb.Slot(1).Status).HasFlag(SlotStatus.Reset));
        Assert.Equal((long)snap, nb.Slot(1).Episode[EpisodeCol.StartEntry]);
        // two restores of one entry evolve identically; stepping them does not move the entry
        for (int t = 0; t < 10; t++)
        {
            int n = nb.Slot(1).CandN;
            int a = n > 0 ? NextIndex(ref rng[1], n) : -1;
            var acts = Enumerable.Repeat(-1, N).ToArray();
            acts[1] = a;
            acts[5] = a;
            env.Step(acts, nb.Table);
            if (env.Slots[1].StepIndex == 0 || env.Slots[5].StepIndex == 0)
            {
                break; // an episode ended; the futures are no longer comparable
            }
            Assert.Equal(env.StateHash(1), env.StateHash(5), $"step {t}: two restores of one snapshot step identically");
        }
        env.Restore(2, snap, nb.Table);
        Assert.Equal(h0, env.StateHash(2), "the entry still holds the snapshot state");

        // at a pending prompt
        var prompt = WishPromptSim();
        int pe = env.Pool.AddSnapshot(prompt.Clone());
        env.Restore(3, pe, nb.Table);
        Assert.True(env.Slots[3].Sim!.HasPendingChoice);
        int ps = env.Snapshot(3);
        env.Restore(4, ps, nb.Table);
        Assert.Equal(prompt.StateHash(), env.StateHash(3));
        Assert.Equal(prompt.StateHash(), env.StateHash(4));
        Assert.True(nb.Slot(3).Cand.SequenceEqual(nb.Slot(4).Cand), "same prompt candidates");
        var step = Enumerable.Repeat(-1, N).ToArray();
        step[3] = 5;
        step[4] = 5;
        env.Step(step, nb.Table);
        var expect = prompt.Clone();
        expect.Step(prompt.LegalActions()[5]);
        Assert.Equal(expect.StateHash(), env.StateHash(3));
        Assert.Equal(expect.StateHash(), env.StateHash(4));
    }

    public static void Test_VecPool_FreshEntriesMatchNewRunAndCacheIsBounded()
    {
        var env = new VecEnv(new VecConfig(1, PoolCache: 2, Pool: new FreshPoolSpec(new[] { "IRONCLAD", "SILENT" }, Count: 4, SeedPrefix: "FR")));
        Assert.Equal(4, env.Pool.Count);
        Assert.Equal("FR-IRONCLAD-0", env.Pool[0].Fresh!.SeedText);
        Assert.Equal("FR-SILENT-1", env.Pool[1].Fresh!.SeedText);
        Assert.Equal("FR-SILENT-3", env.Pool[3].Fresh!.SeedText);
        for (int i = 0; i < 4; i++)
        {
            var f = env.Pool[i].Fresh!;
            ulong expect = RunSim.NewRun(new RunConfig(f.SeedText, f.Character, 0) { Content = ContentFilter.PortedOnly }).StateHash();
            var first = env.Pool.Materialize(i);
            var second = env.Pool.Materialize(i);
            Assert.False(ReferenceEquals(first, second));
            Assert.Equal(expect, first.StateHash(), $"entry {i}: materialised = NewRun");
            Assert.Equal(expect, second.StateHash(), $"entry {i}: materialised again = NewRun");
            first.Step(first.LegalActions()[0]);
            Assert.Equal(expect, env.Pool.Materialize(i).StateHash(), $"entry {i}: stepping a materialised run leaves the template alone");
        }
        Assert.Equal(2, env.Pool.CachedTemplates);
        Assert.Equal(2, env.Pool.CacheLimit);
    }

    /// <summary>
    /// sts2vec_pool_warm (additive, E2c): creates fresh templates ahead of time, in id order, up to max_entries and the
    /// cache limit, returns the count created by the call; warmed starts hash like cold ones (NewRun of the entry).
    /// </summary>
    public static void Test_VecPool_WarmCreatesTemplatesAhead()
    {
        delegate* unmanaged<long, int, int> poolWarm = &Exports.PoolWarm;
        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        try
        {
            const string cfg = "{\"num_envs\": 4, \"threads\": 3, \"seed_text\": \"WARM\", \"pool_cache\": 4, \"pool\": {\"fresh\": {\"characters\": [\"IRONCLAD\", \"SILENT\", \"DEFECT\"], \"count\": 6, \"seed_prefix\": \"WM\"}}}";
            long h = CreateEnv(cfg);
            long cold = CreateEnv(cfg);
            Assert.True(h > 0 && cold > 0, Exports.GlobalError);
            try
            {
                var env = Exports.EnvOf(h)!;
                var coldEnv = Exports.EnvOf(cold)!;
                var snapConfig = new RunConfig("WM-SNAP", "REGENT", 0) { Content = ContentFilter.PortedOnly };
                int snap = env.Pool.AddSnapshot(RunSim.NewRun(snapConfig));
                Assert.Equal(snap, coldEnv.Pool.AddSnapshot(RunSim.NewRun(snapConfig)), "both pools hold the same entries");
                Assert.Equal(0, poolWarm(h, 0), "max_entries 0 warms nothing");
                Assert.Equal(0, env.Pool.CachedTemplates);
                Assert.Equal(2, poolWarm(h, 2), Exports.GlobalError);
                Assert.True(env.Pool[0].HasTemplate && env.Pool[1].HasTemplate && !env.Pool[2].HasTemplate, "id order");
                Assert.Equal(2, poolWarm(h, -1), "the rest, up to pool_cache = 4");
                Assert.Equal(0, poolWarm(h, -1), "the cache is full");
                Assert.Equal(4, env.Pool.CachedTemplates);
                Assert.True(!env.Pool[4].HasTemplate && !env.Pool[5].HasTemplate, "entries beyond the cache stay cold");
                Assert.True(env.Pool[snap].HasTemplate, "a snapshot entry holds its template already");
                for (int i = 0; i < 6; i++)
                {
                    var f = env.Pool[i].Fresh!;
                    ulong expect = RunSim.NewRun(f.ToRunConfig()).StateHash();
                    Assert.Equal(expect, env.Pool.Materialize(i).StateHash(), $"entry {i}: a warmed (or cold) start = NewRun");
                }

                // a warmed env starts and steps exactly like a cold one
                var warm = env;
                using var nw = new NativeBuffers(4);
                using var nc = new NativeBuffers(4);
                warm.Reset(default, default, nw.Table);
                coldEnv.Reset(default, default, nc.Table);
                AssertBuffersEqual(nc, nw, "reset after warm vs cold");
                var rng = SlotRngs(4, 5);
                var actions = new int[4];
                for (int t = 0; t < 30; t++)
                {
                    RandomActions(nw, rng, actions);
                    warm.Step(actions, nw.Table);
                    coldEnv.Step(actions, nc.Table);
                }
                AssertBuffersEqual(nc, nw, "30 steps after warm vs cold");
                Assert.SequenceEqual(Hashes(coldEnv), Hashes(warm), "state hashes");

                Assert.Equal(0, poolWarm(cold, 0));
                Assert.Equal(-1, poolWarm(987654321, -1), "invalid handle");
                Assert.True(Exports.GlobalError.Contains("invalid handle", StringComparison.Ordinal), Exports.GlobalError);
            }
            finally
            {
                Destroy(h);
                Destroy(cold);
            }
        }
        finally
        {
            Exports.RequireServerGC = saved;
        }
    }

    // ------------------------------------------------------------------------------------------- T8 ABI + errors

    private static long CreateEnv(string cfg)
    {
        delegate* unmanaged<byte*, int, long> create = &Exports.Create;
        var bytes = Encoding.UTF8.GetBytes(cfg);
        fixed (byte* p = bytes)
        {
            return create(p, bytes.Length);
        }
    }

    private static void Destroy(long h)
    {
        delegate* unmanaged<long, int> destroy = &Exports.Destroy;
        destroy(h);
    }

    private static string ErrorText(long h, int slot)
    {
        delegate* unmanaged<long, int, byte*, int, int> lastError = &Exports.LastError;
        int n = lastError(h, slot, null, 0);
        if (n <= 0)
        {
            return n < 0 ? "<-1>" : "";
        }
        var buf = new byte[n];
        fixed (byte* p = buf)
        {
            Assert.Equal(n, lastError(h, slot, p, n));
        }
        return Encoding.UTF8.GetString(buf);
    }

    public static void Test_VecExports_InvalidArgumentsNeverCrash()
    {
        delegate* unmanaged<long, int, int> setThreads = &Exports.SetThreads;
        delegate* unmanaged<long, byte*, int, int> addFresh = &Exports.PoolAddFresh;
        delegate* unmanaged<long, byte*, int, int> addSave = &Exports.PoolAddSave;
        delegate* unmanaged<long, int> poolCount = &Exports.PoolCount;
        delegate* unmanaged<long, int, int> snapshot = &Exports.Snapshot;
        delegate* unmanaged<long, int, int, void**, int> restore = &Exports.Restore;
        delegate* unmanaged<long, byte*, int*, void**, int> reset = &Exports.Reset;
        delegate* unmanaged<long, int*, void**, int> step = &Exports.Step;
        delegate* unmanaged<long, int*, int, ulong*, int> stateHash = &Exports.StateHash;
        delegate* unmanaged<long, int, byte*, int, int> lastError = &Exports.LastError;

        int AddFresh(long h, string json)
        {
            var b = Encoding.UTF8.GetBytes(json);
            fixed (byte* p = b)
            {
                return addFresh(h, p, b.Length);
            }
        }

        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        try
        {
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 0}"));
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 2, \"threads\": 0}"));
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 2, \"content\": \"most\"}"));
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 2, \"pool\": {\"fresh\": {\"characters\": [\"NOBODY\"], \"count\": 1}}}"));
            Assert.True(Exports.GlobalError.Contains("NOBODY", StringComparison.Ordinal), Exports.GlobalError);
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 2, \"pool\": {\"fresh\": {\"characters\": [], \"count\": 1}}}"));
            Assert.Equal(-1L, CreateEnv("{\"num_envs\": 2, \"pool\": {\"stale\": {}}}"));
            Assert.Equal(-1L, CreateEnv("[1, 2]"));
            var bad = new byte[] { 0xff, 0xfe, 0x00 };
            fixed (byte* p = bad)
            {
                Assert.Equal(-1L, ((delegate* unmanaged<byte*, int, long>)&Exports.Create)(p, bad.Length));
                Assert.Equal(-1L, ((delegate* unmanaged<byte*, int, long>)&Exports.Create)(null, 5));
                Assert.Equal(-1L, ((delegate* unmanaged<byte*, int, long>)&Exports.Create)(p, -1));
            }

            long h = CreateEnv("{\"num_envs\": 3, \"threads\": 2, \"step_cap\": 50, \"pool\": {\"fresh\": {\"characters\": [\"IRONCLAD\"], \"count\": 2}}}");
            Assert.True(h > 0, Exports.GlobalError);
            try
            {
                using var nb = new NativeBuffers(3);
                Assert.Equal(-1, AddFresh(h, "{\"character\": \"IRONCLAD\"}"), "seed required");
                Assert.True(ErrorText(h, -1).Contains("seed", StringComparison.Ordinal), ErrorText(h, -1));
                Assert.Equal(-1, AddFresh(h, "{\"character\": \"IRONCLAD\", \"seed\": \"S\", \"ascension\": 11}"));
                Assert.Equal(-1, AddFresh(h, "{\"character\": \"IRONCLAD\", \"seed\": \"S\", \"colour\": 1}"));
                Assert.Equal(-1, AddFresh(h, "{\"character\": \"IRONCLAD\", \"seed\": \"S\", \"content\": \"x\"}"));
                Assert.Equal(-1, AddFresh(h, "not json"));
                Assert.Equal(2, AddFresh(h, "{\"character\": \"SILENT\", \"seed\": \"S\", \"ascension\": 3, \"act1\": \"random\"}"), ErrorText(h, -1));
                Assert.Equal(3, poolCount(h));
                Assert.Equal(-1, addSave(h, null, 4));
                Assert.Equal(-1, setThreads(h, -3));
                Assert.Equal(-1, snapshot(h, -1));
                Assert.Equal(-1, snapshot(h, 3));
                Assert.Equal(-1, restore(h, 0, -1, nb.Table));
                Assert.Equal(-1, restore(h, 0, 3, nb.Table));
                Assert.Equal(-1, restore(h, -1, 0, nb.Table));
                Assert.Equal(-1, restore(h, 0, 0, null));
                Assert.Equal(-1, step(h, null, nb.Table));
                int[] acts = { 0, 0, 0 };
                fixed (int* a = acts)
                {
                    Assert.Equal(-1, step(h, a, null));
                }
                // a buffer table with a null entry
                var table = new void*[Buf.Count];
                for (int k = 0; k < Buf.Count; k++) table[k] = nb.Table[k];
                table[Buf.Aux] = null;
                fixed (void** t = table)
                fixed (int* a = acts)
                {
                    Assert.Equal(-1, step(h, a, t));
                    Assert.Equal(-1, reset(h, null, null, t));
                }
                int[] starts = { 0, -7, 3 };
                byte[] mask = { 1, 1, 1 };
                fixed (int* s = starts)
                fixed (byte* m = mask)
                {
                    Assert.Equal(-1, reset(h, m, s, nb.Table), "start id 3 beyond the pool");
                    Assert.True(ErrorText(h, -1).Contains("start_ids[2]", StringComparison.Ordinal), ErrorText(h, -1));
                    mask[2] = 0;
                    Assert.Equal(0, reset(h, m, s, nb.Table), "masked-out slots are not validated; -7 = start sequence");
                }
                Assert.Equal(0L, nb.Slot(2).Episode[EpisodeCol.EpisodeId], "slot 2 was masked out");
                int[] hs = { 0, 1, 2 };
                ulong[] outs = new ulong[3];
                fixed (int* s = hs)
                fixed (ulong* o = outs)
                {
                    Assert.Equal(-1, stateHash(h, s, -1, o));
                    Assert.Equal(-1, stateHash(h, null, 2, o));
                    Assert.Equal(-1, stateHash(h, s, 2, null));
                    Assert.Equal(0, stateHash(h, s, 0, null), "n = 0 needs no pointers");
                    Assert.Equal(0, stateHash(h, s, 3, o));
                }
                Assert.NotEqual(0UL, outs[0]);
                Assert.Equal(0UL, outs[2], "a slot with no run hashes to 0");
                Assert.Equal(-1, lastError(h, -2, null, 0));
                Assert.Equal(-1, lastError(h, 3, null, 0));
                // a truncated read writes exactly cap bytes and still reports the full length
                string full = ErrorText(h, -1);
                var small = new byte[4];
                fixed (byte* p = small)
                {
                    Assert.Equal(Encoding.UTF8.GetByteCount(full), lastError(h, -1, p, 4));
                }
                Assert.SequenceEqual(Encoding.UTF8.GetBytes(full).Take(4), small);

                // the env is still usable after every failure
                fixed (int* a = acts)
                {
                    Assert.Equal(0, step(h, a, nb.Table), ErrorText(h, -1));
                }
                Assert.Equal(0, nb.Slot(0).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap));
            }
            finally
            {
                Destroy(h);
            }
            Assert.Equal(-1, poolCount(h), "destroyed handle");
            Assert.Equal(-1, lastError(h, -1, null, 0));
            Assert.Equal(-1, lastError(-5, 0, null, 0));
        }
        finally
        {
            Exports.RequireServerGC = saved;
        }
    }

    public static void Test_VecEnv_SimErrorResetsSlot()
    {
        foreach (bool auto in new[] { true, false })
        {
            const int N = 4;
            var env = NewEnv(N, 2, 4, autoReset: auto, seedText: "ERR");
            using var nb = new NativeBuffers(N);
            env.Reset(default, default, nb.Table);
            var rng = SlotRngs(N, 11);
            var actions = new int[N];
            for (int t = 0; t < 5; t++)
            {
                RandomActions(nb, rng, actions);
                env.Step(actions, nb.Table);
            }
            var ep = nb.All<long>(Buf.Episode).ToArray();
            int steps1 = env.Slots[1].StepIndex;
            env.FaultInjector = (slot, a) => slot switch
            {
                1 => new InvalidOperationException("injected sim fault"),
                2 => new NotImplementedException("injected content gap"),
                _ => null,
            };
            RandomActions(nb, rng, actions);
            Assert.True(actions.All(a => a >= 0));
            env.Step(actions, nb.Table);
            env.FaultInjector = null;

            var s1 = nb.Slot(1);
            var s2 = nb.Slot(2);
            Assert.True(((SlotStatus)s1.Status).HasFlag(SlotStatus.SimError), $"slot 1 status {s1.Status}");
            Assert.True(((SlotStatus)s2.Status).HasFlag(SlotStatus.ContentGap), $"slot 2 status {s2.Status}");
            Assert.Equal(0, nb.Slot(0).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap));
            Assert.Equal(0, nb.Slot(3).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap));
            foreach (var (s, kind, slot) in new[] { (s1, EndKind.SimError, 1), (s2, EndKind.ContentGap, 2) })
            {
                Assert.Equal(1, (int)s.Truncated, $"slot {slot}: an error ends the episode as truncated");
                Assert.Equal(0, (int)s.Terminated);
                Assert.Equal(0f, s.Reward);
                Assert.Equal((int)kind, s.LastEpisode[LastEpCol.EndKind]);
                Assert.Equal(0, s.LastEpisode[LastEpCol.Win]);
                Assert.True(env.Slots[slot].LastError!.Contains("injected", StringComparison.Ordinal), env.Slots[slot].LastError!);
                if (auto)
                {
                    Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset), $"slot {slot} auto-reset");
                    Assert.Equal(ep[slot * 3 + EpisodeCol.EpisodeId] + N, s.Episode[EpisodeCol.EpisodeId]);
                    Assert.NotNull(env.Slots[slot].Sim);
                    Assert.True(s.CandN > 0);
                }
                else
                {
                    Assert.Null(env.Slots[slot].Sim);
                    Assert.Equal(0, s.CandN);
                    Assert.Equal(0UL, env.StateHash(slot));
                }
            }
            Assert.Equal(steps1, s1.LastEpisode[LastEpCol.Steps], "the failed step does not count");

            // the slot keeps working afterwards
            if (!auto)
            {
                env.Reset(new byte[] { 0, 1, 1, 0 }, default, nb.Table);
            }
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
            Assert.Equal(0, nb.Slot(1).Status & (int)(SlotStatus.SimError | SlotStatus.IllegalAction));
            Assert.Equal(0, nb.Slot(2).Status & (int)(SlotStatus.ContentGap | SlotStatus.IllegalAction));
        }
    }
}
