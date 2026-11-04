using System.Runtime.CompilerServices;
using System.Text;
using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Search branching (ABI 2): <c>sts2vec_branch</c> (exact / determinized copies of a live slot into other slots, no pool
/// entry), <c>sts2vec_restore_many</c> (batched restore), <c>sts2vec_pool_release</c> (released entries, never reused) and
/// the lock semantics of concurrent calls. sim/VEC.md "Search".
/// </summary>
public static unsafe partial class VecTests
{
    /// <summary>Every buffer a branch writes from the reference view (all but the per-slot signals / bookkeeping).</summary>
    private static readonly int[] ViewBuffers = Enumerable.Range(0, Buf.Count)
        .Where(k => k is not (Buf.Reward or Buf.Terminated or Buf.Truncated or Buf.Status or Buf.Episode or Buf.LastEpisode))
        .ToArray();

    private const SlotStatus ViewBits = SlotStatus.TableOverflow | SlotStatus.CandidateOverflow;

    private static byte[][] CopyAll(NativeBuffers nb) => Enumerable.Range(0, Buf.Count).Select(k => nb.AllBytes(k).ToArray()).ToArray();

    private static bool RowEqual(NativeBuffers nb, int k, int a, int b) => nb.Slot(a).Bytes(k).SequenceEqual(nb.Slot(b).Bytes(k));

    /// <summary>The first view buffer whose rows differ between slot a of x and slot b of y (null when none does).</summary>
    private static string? ViewDifference(NativeBuffers x, int a, NativeBuffers y, int b)
    {
        foreach (int k in ViewBuffers)
        {
            if (!x.Slot(a).Bytes(k).SequenceEqual(y.Slot(b).Bytes(k)))
            {
                return Schema.Buffers[k].Name;
            }
        }
        return null;
    }

    /// <summary>Slot rows of every buffer except <paramref name="slots"/> are byte-identical to <paramref name="before"/>.</summary>
    private static void AssertOtherRowsUntouched(NativeBuffers nb, byte[][] before, ISet<int> slots, string what)
    {
        for (int k = 0; k < Buf.Count; k++)
        {
            int w = Schema.BytesPerEnv(k);
            var now = nb.AllBytes(k);
            for (int i = 0; i < nb.NumEnvs; i++)
            {
                if (!slots.Contains(i) && !now.Slice(i * w, w).SequenceEqual(before[k].AsSpan(i * w, w)))
                {
                    Assert.Fail($"{what}: buffer {Schema.Buffers[k].Name} of slot {i} changed");
                }
            }
        }
    }

    /// <summary>
    /// The observation the env writes for <paramref name="sim"/> on its own: restored into slot 0 of a separate one-slot
    /// env (through a pool entry, then released), so the comparison does not depend on the encoder or candidate API.
    /// </summary>
    private static void ObserveAlone(VecEnv probe, NativeBuffers probeNb, RunSim sim)
    {
        int e = probe.Pool.AddSnapshot(sim.Clone());
        probe.Restore(0, e, probeNb.Table);
        probe.ReleaseEntries(new[] { e });
    }

    /// <summary>
    /// Exact branch: every destination holds a hash-identical copy of the source (pending prompts and candidate overflow
    /// included), shows the source's view byte for byte (= its own encoding), starts a new episode (RESET, step index 0,
    /// start_entry -1, episode id advanced by N, last_episode untouched), every other row is untouched, and the copies
    /// replay the source's future step for step.
    /// </summary>
    public static void Test_VecBranch_ExactCopiesStateViewAndFuture()
    {
        const int N = 12;
        var env = NewEnv(N, 4, 8, seedText: "BRX");
        var probe = new VecEnv(new VecConfig(1, Threads: 1));
        using var nb = new NativeBuffers(N);
        using var pnb = new NativeBuffers(1);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 21);
        var actions = new int[N];
        int[] dsts = { 3, 7, 10, 11 };
        int points = 0, prompts = 0, combat = 0, replayed = 0;

        void CheckBranch(int src, string where)
        {
            var before = CopyAll(nb);
            ulong h = env.StateHash(src);
            int srcStatus = nb.Slot(src).Status;
            var episodes = dsts.Select(d => env.Slots[d].EpisodeCount).ToArray();
            var srcSlot = env.Slots[src];
            (long id, int step, int floorSteps) srcCounters = (srcSlot.EpisodeId, srcSlot.StepIndex, srcSlot.FloorSteps);
            env.Branch(src, dsts, BranchMode.Exact, 0, nb.Table);
            AssertOtherRowsUntouched(nb, before, new HashSet<int>(dsts), $"{where}: exact branch");
            Assert.Equal(srcCounters, (srcSlot.EpisodeId, srcSlot.StepIndex, srcSlot.FloorSteps), $"{where}: source counters");
            Assert.Equal(h, env.StateHash(src), $"{where}: the source is not modified");
            for (int j = 0; j < dsts.Length; j++)
            {
                int d = dsts[j];
                var s = env.Slots[d];
                Assert.Equal(h, env.StateHash(d), $"{where}: dst {d} hash");
                Assert.Null(ViewDifference(nb, d, nb, src), $"{where}: dst {d} shows the source's view");
                ObserveAlone(probe, pnb, s.Sim!);
                Assert.Null(ViewDifference(nb, d, pnb, 0), $"{where}: dst {d} view = its own encoding");
                var b = nb.Slot(d);
                Assert.Equal((int)SlotStatus.Reset | (srcStatus & (int)ViewBits), b.Status, $"{where}: dst {d} status");
                Assert.Equal(0f, b.Reward);
                Assert.Equal(0, (int)b.Terminated + b.Truncated);
                Assert.Equal(episodes[j] * N + d + 1, b.Episode[EpisodeCol.EpisodeId], $"{where}: dst {d} episode id");
                Assert.Equal(episodes[j] + 1, s.EpisodeCount);
                Assert.Equal(0L, b.Episode[EpisodeCol.StepIndex]);
                Assert.Equal(-1L, b.Episode[EpisodeCol.StartEntry], "a branched episode has no pool entry");
                Assert.True(s.StepIndex == 0 && s.FloorSteps == 0 && !s.Finished && s.StartEntry == -1, $"{where}: dst {d} counters");
                int lw = Schema.BytesPerEnv(Buf.LastEpisode);
                Assert.True(b.Bytes(Buf.LastEpisode).SequenceEqual(before[Buf.LastEpisode].AsSpan(d * lw, lw)), $"{where}: dst {d} last_episode untouched");
            }
            points++;
        }

        for (int t = 0; t < 150; t++)
        {
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
            if (t % 6 != 5)
            {
                continue;
            }
            int src = t % 3;
            var sim = env.Slots[src].Sim!;
            prompts += sim.HasPendingChoice ? 1 : 0;
            combat += sim.State.Combat is { IsInProgress: true } ? 1 : 0;
            CheckBranch(src, $"t {t} src {src}");
            // replay: the source and its copies take the same actions; everything else is a no-op
            for (int r = 0; r < 12; r++)
            {
                int n = nb.Slot(src).CandN;
                if (n == 0)
                {
                    break;
                }
                var acts = Enumerable.Repeat(-1, N).ToArray();
                int a = NextIndex(ref rng[src], n);
                acts[src] = a;
                foreach (int d in dsts)
                {
                    acts[d] = a;
                }
                env.Step(acts, nb.Table);
                if (env.Slots[src].StepIndex == 0)
                {
                    break; // the source's episode ended and auto-reset: the futures are no longer comparable
                }
                foreach (int d in dsts)
                {
                    Assert.Equal(env.StateHash(src), env.StateHash(d), $"t {t} replay step {r}: dst {d}");
                    Assert.Null(ViewDifference(nb, d, nb, src), $"t {t} replay step {r}: dst {d} view");
                }
                replayed++;
            }
        }
        Assert.True(points >= 20 && combat >= 5 && replayed >= 100, $"points {points}, combat {combat}, replayed {replayed}");
        Console.WriteLine($"     exact branch: {points} source states ({combat} in combat) x {dsts.Length} copies, {replayed} replayed steps identical");

        // a pending prompt with more legal actions than CAP_CANDIDATES: the copies keep the prompt, the tape and the
        // CANDIDATE_OVERFLOW bit, and answer it like the source
        var wish = WishPromptSim();
        env.Restore(1, env.Pool.AddSnapshot(wish.Clone()), nb.Table);
        Assert.True(((SlotStatus)nb.Slot(1).Status).HasFlag(SlotStatus.CandidateOverflow), "the WISH prompt overflows");
        CheckBranch(1, "WISH prompt");
        foreach (int d in dsts)
        {
            Assert.True(env.Slots[d].Sim!.HasPendingChoice && ((SlotStatus)nb.Slot(d).Status).HasFlag(SlotStatus.CandidateOverflow));
        }
        var pick = Enumerable.Repeat(-1, N).ToArray();
        pick[1] = 5;
        foreach (int d in dsts)
        {
            pick[d] = 5;
        }
        env.Step(pick, nb.Table);
        var expect = wish.Clone();
        expect.Step(wish.LegalActions()[5]);
        foreach (int s in dsts.Append(1))
        {
            Assert.Equal(expect.StateHash(), env.StateHash(s), $"slot {s} answered the prompt like the source");
        }
        prompts++;
        Assert.True(prompts >= 1, "prompt coverage");
    }

    /// <summary>
    /// Determinized branch: every destination shows the source's view byte for byte, and that copied view is the
    /// destination's own encoding (the player-view contract the single-encode copy relies on, checked on every
    /// destination); its hidden state is resampled (hash differs from the source and between destinations); the result is
    /// a function of (state, seed, position): the same call into other slots gives the same hashes, and position j equals
    /// <c>CloneDeterminized(BranchSeed(seed, j))</c> restored through a pool entry (the pre-branch path).
    /// </summary>
    public static void Test_VecBranch_DeterminizedSameViewResampledHiddenState()
    {
        foreach (int ascension in new[] { 0, 10 })
        {
            const int N = 10;
            var env = NewEnv(N, 4, 10, seedText: $"BRD{ascension}", ascension: ascension);
            var probe = new VecEnv(new VecConfig(1, Threads: 1));
            using var nb = new NativeBuffers(N);
            using var pnb = new NativeBuffers(1);
            env.Reset(default, default, nb.Table);
            var rng = SlotRngs(N, 33 + (ulong)ascension);
            var actions = new int[N];
            int[] dsts = { 4, 5, 6, 7 };
            int[] again = { 9, 8 };
            int points = 0, prompts = 0, nonCombat = 0, combat = 0;
            for (int t = 0; t < 360; t++)
            {
                for (int i = 0; i < N; i++)
                {
                    actions[i] = i < 3 ? PlayBiased(nb, i, ref rng[i]) : -1;
                }
                env.Step(actions, nb.Table);
                if (t % 3 != 2)
                {
                    continue;
                }
                int src = (t / 3) % 3;
                var sim = env.Slots[src].Sim!;
                if (sim.HasPendingChoice) prompts++;
                else if (sim.State.Combat is { IsInProgress: true }) combat++;
                else nonCombat++;
                ulong seed = 0xC0FFEE00UL + (ulong)t;
                ulong h = env.StateHash(src);
                var before = CopyAll(nb);
                env.Branch(src, dsts, BranchMode.Determinized, seed, nb.Table);
                AssertOtherRowsUntouched(nb, before, new HashSet<int>(dsts), $"A{ascension} t {t}");
                var hashes = new ulong[dsts.Length];
                for (int j = 0; j < dsts.Length; j++)
                {
                    int d = dsts[j];
                    hashes[j] = env.StateHash(d);
                    Assert.Null(ViewDifference(nb, d, nb, src), $"A{ascension} t {t}: dst {d} shows the source's view");
                    ObserveAlone(probe, pnb, env.Slots[d].Sim!);
                    Assert.Null(ViewDifference(nb, d, pnb, 0), $"A{ascension} t {t}: dst {d}'s copied view = its own encoding");
                    Assert.Equal((int)SlotStatus.Reset | (BitConverter.ToInt32(before[Buf.Status], src * 4) & (int)ViewBits), nb.Slot(d).Status);
                    Assert.NotEqual(h, hashes[j], $"A{ascension} t {t}: dst {d} hidden state resampled");
                    Assert.Equal(sim.CloneDeterminized(VecEnv.BranchSeed(seed, j)).StateHash(), hashes[j], $"A{ascension} t {t}: position {j} seed");
                }
                Assert.Equal(dsts.Length, hashes.Distinct().Count(), $"A{ascension} t {t}: destinations differ from each other");
                Assert.Equal(h, env.StateHash(src), "the source is not modified");
                if (t % 9 == 2)
                {
                    // the same branch into other slots (positions 0 and 1) and the pre-branch path give the same states
                    env.Branch(src, again, BranchMode.Determinized, seed, nb.Table);
                    Assert.Equal(hashes[0], env.StateHash(again[0]));
                    Assert.Equal(hashes[1], env.StateHash(again[1]));
                    int e = env.CloneDeterminized(src, VecEnv.BranchSeed(seed, 1));
                    env.Restore(9, e, nb.Table);
                    Assert.Equal(hashes[1], env.StateHash(9), "branch position 1 = clone_determinized(BranchSeed(seed, 1)) + restore");
                    Assert.True(RowEqual(nb, Buf.Cand, 9, dsts[1]) && RowEqual(nb, Buf.GlobalI, 9, dsts[1]));
                    env.ReleaseEntries(new[] { e });
                }
                points++;
            }
            Assert.Equal(10, env.Pool.Count, "every entry the test added was released");
            Assert.True(points >= 100 && combat >= 30 && nonCombat >= 20, $"A{ascension}: points {points}, combat {combat}, non-combat {nonCombat}, prompts {prompts}");
            Console.WriteLine($"     determinized branch A{ascension}: {points} source states ({combat} combat, {nonCombat} screens, {prompts} prompts) x {dsts.Length} destinations: copied view = own encoding, hashes resampled");
        }
        // a pending prompt with more options than CAP_CANDIDATES (WISH's PileSelect over a 250-card draw pile): every
        // determinized copy keeps the prompt and its CANDIDATE_OVERFLOW bit, its copied view is its own encoding, its hidden
        // state differs, and every copy answers the prompt (the tape replays; no SIM_ERROR)
        {
            const int N = 6;
            var env = NewEnv(N, 4, 0, seedText: "BRDW");
            var probe = new VecEnv(new VecConfig(1, Threads: 1));
            using var nb = new NativeBuffers(N);
            using var pnb = new NativeBuffers(1);
            var wish = WishPromptSim();
            env.Restore(0, env.Pool.AddSnapshot(wish.Clone()), nb.Table);
            int[] dsts = { 1, 2, 3, 4, 5 };
            env.Branch(0, dsts, BranchMode.Determinized, 99, nb.Table);
            ulong h = env.StateHash(0);
            foreach (int d in dsts)
            {
                Assert.True(env.Slots[d].Sim!.HasPendingChoice, $"dst {d} keeps the prompt");
                Assert.Equal((int)(SlotStatus.Reset | SlotStatus.CandidateOverflow | SlotStatus.TableOverflow), nb.Slot(d).Status,
                    $"dst {d} status (192 candidates of 250, 128 prompt_options rows)");
                Assert.Null(ViewDifference(nb, d, nb, 0), $"dst {d} shows the prompt");
                ObserveAlone(probe, pnb, env.Slots[d].Sim!);
                Assert.Null(ViewDifference(nb, d, pnb, 0), $"dst {d}'s copied view = its own encoding");
                Assert.NotEqual(h, env.StateHash(d));
            }
            env.Step(Enumerable.Repeat(5, N).ToArray(), nb.Table);
            for (int i = 0; i < N; i++)
            {
                Assert.Equal(0, nb.Slot(i).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap | SlotStatus.IllegalAction),
                    $"slot {i} answered the prompt: {env.Slots[i].LastError}");
            }
        }

        // BranchSeed test vectors (sts2vec.branch_seed in Python reproduces them)
        Assert.Equal(0xE220A8397B1DCDAFUL, VecEnv.BranchSeed(0, 0));
        Assert.Equal(0x6E789E6AA1B965F4UL, VecEnv.BranchSeed(0, 1));
        Assert.Equal(0xE4D971771B652C20UL, VecEnv.BranchSeed(ulong.MaxValue, 0));
        Assert.Equal(0x6E7411B06820371CUL, VecEnv.BranchSeed(12345, 7));
    }

    /// <summary>
    /// branch_many: several roots (interleaved in the call) branch in one call; each destination equals its own source
    /// (exact: hash and view; determinized: view, and the hash of <c>CloneDeterminized(BranchSeed(seed, j))</c> with j the
    /// position in the call), every copied view is the destination's own encoding, a single-source branch_many equals
    /// branch, and invalid pairs fail before anything changes.
    /// </summary>
    public static void Test_VecBranch_ManySourcesInOneCall()
    {
        const int N = 16;
        var env = NewEnv(N, 4, 8, seedText: "BRM");
        var probe = new VecEnv(new VecConfig(1, Threads: 1));
        using var nb = new NativeBuffers(N);
        using var pnb = new NativeBuffers(1);
        var mask = Enumerable.Repeat((byte)1, N).ToArray();
        mask[15] = 0; // slot 15 holds no run
        env.Reset(mask, default, nb.Table);
        var rng = SlotRngs(N, 41);
        var actions = new int[N];
        int rounds = 0;
        int[] srcs = { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 };
        int[] dsts = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        for (int t = 0; t < 90; t++)
        {
            RandomActions(nb, rng, actions);
            actions[15] = -1;
            env.Step(actions, nb.Table);
            if (t % 15 != 14)
            {
                continue;
            }
            foreach (var mode in new[] { BranchMode.Exact, BranchMode.Determinized })
            {
                ulong seed = (ulong)(t * 7 + 1);
                var before = CopyAll(nb);
                var hs = srcs.Distinct().ToDictionary(x => x, env.StateHash);
                env.BranchMany(srcs, dsts, mode, seed, nb.Table);
                AssertOtherRowsUntouched(nb, before, new HashSet<int>(dsts), $"t {t} {mode}");
                for (int j = 0; j < dsts.Length; j++)
                {
                    int d = dsts[j], src = srcs[j];
                    var expect = mode == BranchMode.Exact ? hs[src] : env.Slots[src].Sim!.CloneDeterminized(VecEnv.BranchSeed(seed, j)).StateHash();
                    Assert.Equal(expect, env.StateHash(d), $"t {t} {mode}: dst {d} of src {src}");
                    Assert.Null(ViewDifference(nb, d, nb, src), $"t {t} {mode}: dst {d} shows src {src}'s view");
                    ObserveAlone(probe, pnb, env.Slots[d].Sim!);
                    Assert.Null(ViewDifference(nb, d, pnb, 0), $"t {t} {mode}: dst {d}'s view = its own encoding");
                    Assert.True(env.Slots[d].StepIndex == 0 && env.Slots[d].StartEntry == -1 && ((SlotStatus)nb.Slot(d).Status).HasFlag(SlotStatus.Reset));
                }
                foreach (var (src, h) in hs)
                {
                    Assert.Equal(h, env.StateHash(src), "sources are not modified");
                }
            }
            // a single-source branch_many is branch
            env.Branch(0, new[] { 3, 4, 5 }, BranchMode.Determinized, 5, nb.Table);
            env.BranchMany(new[] { 0, 0, 0 }, new[] { 6, 7, 8 }, BranchMode.Determinized, 5, nb.Table);
            Assert.SequenceEqual(new[] { 3, 4, 5 }.Select(env.StateHash), new[] { 6, 7, 8 }.Select(env.StateHash), "branch = branch_many");
            for (int k = 0; k < Buf.Count; k++)
            {
                if (ViewBuffers.Contains(k) || k == Buf.Status)
                {
                    Assert.True(RowEqual(nb, k, 3, 6) && RowEqual(nb, k, 5, 8), Schema.Buffers[k].Name);
                }
            }
            rounds++;
        }
        Assert.Equal(6, rounds);

        var snap = CopyAll(nb);
        var hashes = Hashes(env);
        foreach (var (s, d, text) in new (int[], int[], string)[]
        {
            (new[] { 0, 1 }, new[] { 3 }, "src_slots has 2 entries but dst_slots has 1"),
            (new[] { 0, 99 }, new[] { 3, 4 }, "src_slots[1] = 99 outside"),
            (new[] { 0, 15 }, new[] { 3, 4 }, "src_slots[1]: slot 15 holds no run"),
            (new[] { 0, 1 }, new[] { 3, 1 }, "dst_slots[1] = 1 is a source slot of this call"),
            (new[] { 0, 1 }, new[] { 3, 0 }, "dst_slots[1] = 0 is a source slot of this call"),
            (new[] { 0, 1 }, new[] { 3, 3 }, "dst_slots[1] = 3 is listed twice"),
            (new[] { 0, 1 }, new[] { 3, 16 }, "dst_slots[1] = 16 outside"),
        })
        {
            try
            {
                env.BranchMany(s, d, BranchMode.Exact, 0, nb.Table);
                Assert.Fail($"branch_many([{string.Join(",", s)}], [{string.Join(",", d)}]) did not throw");
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                Assert.True(e.Message.Contains(text, StringComparison.Ordinal), $"'{text}' not in: {e.Message}");
            }
        }
        AssertBuffersEqual(nb, Inline(snap), "failed branch_many calls change nothing");
        Assert.SequenceEqual(hashes, Hashes(env));
    }

    /// <summary>
    /// Through the export: counters (episode ids advance by N per branch), a finished slot can be branched, n = 0 is a
    /// no-op, and every invalid argument fails with -1 and a message before anything changes.
    /// </summary>
    public static void Test_VecBranch_CountersAndInvalidArgumentsThroughTheAbi()
    {
        delegate* unmanaged<long, int, int*, int, int, ulong, void**, int> branch = &Exports.Branch;
        delegate* unmanaged<long, int*, void**, int> step = &Exports.Step;
        delegate* unmanaged<long, byte*, int*, void**, int> reset = &Exports.Reset;
        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        try
        {
            const int N = 6;
            long h = CreateEnv("{\"num_envs\": 6, \"threads\": 2, \"seed_text\": \"BRV\", \"auto_reset\": false, \"step_cap\": 8, " +
                               "\"pool\": {\"fresh\": {\"characters\": [\"IRONCLAD\", \"DEFECT\"], \"count\": 4}}}");
            Assert.True(h > 0, Exports.GlobalError);
            try
            {
                var env = Exports.EnvOf(h)!;
                using var nb = new NativeBuffers(N);
                byte[] mask = { 1, 1, 1, 1, 0, 0 };
                fixed (byte* m = mask)
                {
                    Assert.Equal(0, reset(h, m, null, nb.Table), Exports.GlobalError);
                }
                int Branch(int src, int[] dsts, int n, int mode, ulong seed, void** table)
                {
                    fixed (int* d = dsts)
                    {
                        return branch(h, src, dsts.Length == 0 ? null : d, n, mode, seed, table);
                    }
                }

                // counters: two branches into slot 2 advance its episode id by N each time
                Assert.Equal(0, Branch(0, new[] { 2, 3 }, 2, 0, 0, nb.Table), Exports.GlobalError);
                Assert.Equal(1L * N + 2 + 1, nb.Slot(2).Episode[EpisodeCol.EpisodeId]);
                Assert.Equal(0, Branch(0, new[] { 2 }, 1, 1, 7, nb.Table), Exports.GlobalError);
                Assert.Equal(2L * N + 2 + 1, nb.Slot(2).Episode[EpisodeCol.EpisodeId]);
                Assert.Equal(1L * N + 3 + 1, nb.Slot(3).Episode[EpisodeCol.EpisodeId]);
                Assert.Equal(0, Branch(0, Array.Empty<int>(), 0, 0, 0, nb.Table), "n = 0 is a no-op");

                // a finished slot (auto_reset off, step cap reached) can be branched: the copies are live again
                var rng = SlotRngs(N, 5);
                var acts = new int[N];
                for (int t = 0; t < 8; t++)
                {
                    RandomActions(nb, rng, acts);
                    acts[4] = acts[5] = -1;
                    fixed (int* a = acts)
                    {
                        Assert.Equal(0, step(h, a, nb.Table));
                    }
                }
                Assert.True(env.Slots[1].Finished && nb.Slot(1).CandN == 0, "slot 1 hit the step cap");
                Assert.Equal(0, Branch(1, new[] { 4 }, 1, 0, 0, nb.Table), Exports.GlobalError);
                Assert.True(!env.Slots[4].Finished && nb.Slot(4).CandN > 0 && env.StateHash(4) == env.StateHash(1));

                // invalid arguments: -1 + text, nothing changes
                Assert.Equal(-1, Branch(5, new[] { 2 }, 1, 0, 0, nb.Table), "slot 5 holds no run");
                Assert.True(ErrorText(h, -1).Contains("holds no run", StringComparison.Ordinal), ErrorText(h, -1));
                var before = CopyAll(nb);
                var hashes = Hashes(env);
                var counts = env.Slots.Select(s => s.EpisodeCount).ToArray();
                (int src, int[] dsts, int n, int mode, string text)[] bad =
                {
                    (-1, new[] { 2 }, 1, 0, "outside"),
                    (6, new[] { 2 }, 1, 0, "outside"),
                    (0, new[] { 6 }, 1, 0, "dst_slots[0] = 6 outside"),
                    (0, new[] { 2, -1 }, 2, 0, "dst_slots[1] = -1 outside"),
                    (0, new[] { 2, 0 }, 2, 0, "is a source slot"),
                    (0, new[] { 2, 3, 2 }, 3, 0, "listed twice"),
                    (0, new[] { 2 }, 1, 2, "mode"),
                    (0, new[] { 2 }, 1, -1, "mode"),
                    (0, new[] { 2 }, -1, 0, "n"),
                    (0, new[] { 2, 3, 4, 5, 1, 2, 3 }, 7, 0, "n"),
                    (0, Array.Empty<int>(), 1, 0, "non-null dst_slots"),
                };
                foreach (var (src, dsts, n, mode, text) in bad)
                {
                    Assert.Equal(-1, Branch(src, dsts, n, mode, 0, nb.Table), $"branch({src}, [{string.Join(",", dsts)}], {n}, {mode})");
                    Assert.True(ErrorText(h, -1).Contains(text, StringComparison.Ordinal), $"'{text}' not in: {ErrorText(h, -1)}");
                }
                Assert.Equal(-1, Branch(0, new[] { 2 }, 1, 0, 0, null), "null buffers");
                Assert.Equal(-1, branch(987654321, 0, null, 0, 0, 0, nb.Table), "invalid handle");
                AssertBuffersEqual(nb, Inline(before), "failed branches change nothing");
                Assert.SequenceEqual(hashes, Hashes(env), "failed branches change no state");
                Assert.SequenceEqual(counts, env.Slots.Select(s => s.EpisodeCount).ToArray(), "failed branches start no episode");
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

    /// <summary>A NativeBuffers holding <paramref name="bytes"/> (one array per buffer), for byte comparisons.</summary>
    private static NativeBuffers Inline(byte[][] bytes)
    {
        int n = bytes[0].Length / Schema.BytesPerEnv(0);
        var nb = new NativeBuffers(n);
        for (int k = 0; k < Buf.Count; k++)
        {
            bytes[k].CopyTo(nb.AllBytes(k));
        }
        return nb;
    }

    /// <summary>
    /// Released entries: pool_count and the start sequence drop them, their ids are never reused and every later use is an
    /// error (restore, reset, restore_many, release); a release is all or nothing; a released snapshot's state is garbage
    /// collected; a released fresh template frees its cache slot; a slot started from a released entry keeps running.
    /// </summary>
    public static void Test_VecPool_ReleaseDropsEntriesForGood()
    {
        const int N = 4;
        var env = NewEnv(N, 2, 4, seedText: "REL");
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 9);
        var actions = new int[N];
        for (int t = 0; t < 12; t++)
        {
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
        }
        int a = env.Snapshot(0), b = env.Snapshot(1), c = env.CloneDeterminized(2, 5);
        Assert.SequenceEqual(new[] { 4, 5, 6 }, new[] { a, b, c });
        env.Restore(3, c, nb.Table);
        ulong h3 = env.StateHash(3);
        Assert.Equal(2, env.ReleaseEntries(new[] { a, c }));
        Assert.Equal(5, env.Pool.Count);
        Assert.Equal(7, env.Pool.IdsIssued);
        Assert.SequenceEqual(new[] { 0, 1, 2, 3, 5 }, env.Pool.LiveIds());
        Assert.True(!env.Pool.IsLive(a) && env.Pool.IsLive(b));
        Assert.True(env.Pool.Problem(c)!.Contains("released", StringComparison.Ordinal));
        Assert.True(env.Pool.Problem(7)!.Contains("does not exist", StringComparison.Ordinal));

        void Throws(Action act, string text, string what)
        {
            try
            {
                act();
            }
            catch (ArgumentException e)
            {
                Assert.True(e.Message.Contains(text, StringComparison.Ordinal), $"{what}: '{text}' not in: {e.Message}");
                return;
            }
            Assert.Fail($"{what}: no exception");
        }
        var before = CopyAll(nb);
        Throws(() => _ = env.Pool[a], "released", "indexer");
        Throws(() => env.Restore(2, a, nb.Table), "released", "restore");
        Throws(() => env.Reset(new byte[] { 0, 1, 0, 0 }, new[] { -1, c, -1, -1 }, nb.Table), "start_ids[1] = 6: pool entry 6 was released", "reset");
        Throws(() => env.RestoreMany(new[] { 1, 2 }, new[] { b, a }, nb.Table), "entries[1] = 4", "restore_many");
        Throws(() => env.ReleaseEntries(new[] { b, a }), "entries[1] = 4: pool entry 4 was released", "release of a released id");
        Throws(() => env.ReleaseEntries(new[] { b, b }), "listed twice", "duplicate release");
        Throws(() => env.ReleaseEntries(new[] { 99 }), "does not exist", "never issued");
        AssertBuffersEqual(nb, Inline(before), "failed calls change nothing");
        Assert.True(env.Pool.IsLive(b), "a failed release releases nothing");

        // the slot restored from a released entry keeps running
        Assert.Equal(h3, env.StateHash(3));
        var step = Enumerable.Repeat(-1, N).ToArray();
        step[3] = 0;
        env.Step(step, nb.Table);
        Assert.Equal(0, nb.Slot(3).Status & (int)(SlotStatus.SimError | SlotStatus.IllegalAction));

        // ids are never reused; the start sequence runs over the live entries in id order
        int d = env.Snapshot(0);
        Assert.Equal(7, d);
        var live = env.Pool.LiveIds();
        Assert.SequenceEqual(new[] { 0, 1, 2, 3, 5, 7 }, live);
        var expect = env.Slots.Select(s => live[(int)((s.SlotSeed + (ulong)s.EpisodeCount) % (ulong)live.Length)]).ToArray();
        env.Reset(default, default, nb.Table);
        Assert.SequenceEqual(expect, env.Slots.Select(s => s.StartEntry).ToArray(), "sequence over live entries");
        Assert.SequenceEqual(expect.Select(x => (long)x).ToArray(), Enumerable.Range(0, N).Select(i => nb.Slot(i).Episode[EpisodeCol.StartEntry]).ToArray());

        // a released snapshot's RunSim is collected
        var (weakId, weak) = AddWeakSnapshot(env);
        Assert.True(weak.IsAlive);
        env.ReleaseEntries(new[] { weakId });
        for (int g = 0; g < 3 && weak.IsAlive; g++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(weak.IsAlive, "the released snapshot is garbage");

        // a released fresh template frees its cache slot
        var cached = new VecEnv(new VecConfig(1, PoolCache: 2, Pool: new FreshPoolSpec(new[] { "IRONCLAD" }, Count: 3, SeedPrefix: "RC")));
        Assert.Equal(2, cached.Pool.Warm(-1, 1));
        Assert.Equal(2, cached.Pool.CachedTemplates);
        cached.ReleaseEntries(new[] { 0 });
        Assert.Equal(1, cached.Pool.CachedTemplates);
        Assert.Equal(1, cached.Pool.Warm(-1, 1));
        Assert.True(cached.Pool[2].HasTemplate && cached.Pool.CachedTemplates == 2, "entry 2 took the freed cache slot");

        // through the export: returns n; -1 with text and nothing released on a bad id
        delegate* unmanaged<long, int*, int, int> release = &Exports.PoolRelease;
        delegate* unmanaged<long, int> poolCount = &Exports.PoolCount;
        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        try
        {
            long h = CreateEnv("{\"num_envs\": 1, \"pool\": {\"fresh\": {\"characters\": [\"SILENT\"], \"count\": 5}}}");
            try
            {
                int[] ids = { 4, 1 };
                fixed (int* p = ids)
                {
                    Assert.Equal(2, release(h, p, 2), Exports.GlobalError);
                    Assert.Equal(3, poolCount(h));
                    Assert.Equal(-1, release(h, p, 1), "already released");
                    Assert.True(ErrorText(h, -1).Contains("was released", StringComparison.Ordinal), ErrorText(h, -1));
                    Assert.Equal(-1, release(h, p, -1));
                    Assert.Equal(-1, release(h, null, 1));
                    Assert.Equal(0, release(h, null, 0));
                }
                Assert.Equal(-1, release(987654321, null, 0), "invalid handle");
                Assert.Equal(3, poolCount(h));
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Id, WeakReference Weak) AddWeakSnapshot(VecEnv env)
    {
        var sim = RunSim.NewRun(new RunConfig("REL-WEAK", "REGENT", 0) { Content = ContentFilter.PortedOnly });
        return (env.Pool.AddSnapshot(sim), new WeakReference(sim));
    }

    /// <summary>
    /// restore_many ≡ the same restores one by one (buffers and hashes, then 20 identical steps), including several slots
    /// from one entry (cloned in parallel) and a pending-prompt entry; invalid pairs fail before anything changes.
    /// </summary>
    public static void Test_VecRestoreMany_EqualsSequentialRestores()
    {
        const int N = 10;
        VecEnv Make() => NewEnv(N, 4, 6, seedText: "RMANY");
        var many = Make();
        var seq = Make();
        using var nm = new NativeBuffers(N);
        using var ns = new NativeBuffers(N);
        many.Reset(default, default, nm.Table);
        seq.Reset(default, default, ns.Table);
        var rm = SlotRngs(N, 17);
        var rs = SlotRngs(N, 17);
        var am = new int[N];
        var asq = new int[N];
        for (int t = 0; t < 25; t++)
        {
            RandomActions(nm, rm, am);
            RandomActions(ns, rs, asq);
            many.Step(am, nm.Table);
            seq.Step(asq, ns.Table);
        }
        var wish = WishPromptSim();
        int[] entries = new int[4];
        foreach (var env in new[] { many, seq })
        {
            entries[0] = env.Snapshot(0);
            entries[1] = env.CloneDeterminized(1, 11);
            entries[2] = env.Pool.AddSnapshot(wish.Clone());
            entries[3] = 2;
        }
        int[] slots = { 7, 2, 9, 0, 5, 4, 8, 1 };
        int[] from = { entries[0], entries[0], entries[0], entries[1], entries[2], entries[2], entries[3], entries[1] };
        many.RestoreMany(slots, from, nm.Table);
        for (int j = 0; j < slots.Length; j++)
        {
            seq.Restore(slots[j], from[j], ns.Table);
        }
        AssertBuffersEqual(ns, nm, "restore_many vs sequential restores");
        Assert.SequenceEqual(Hashes(seq), Hashes(many), "state hashes");
        Assert.True(((SlotStatus)nm.Slot(5).Status).HasFlag(SlotStatus.CandidateOverflow), "the prompt entry restored with its overflow");
        Assert.Equal(many.StateHash(7), many.StateHash(2));
        Assert.Equal(many.StateHash(7), many.StateHash(9));
        for (int t = 0; t < 20; t++)
        {
            RandomActions(nm, rm, am);
            RandomActions(ns, rs, asq);
            many.Step(am, nm.Table);
            seq.Step(asq, ns.Table);
        }
        AssertBuffersEqual(ns, nm, "20 steps later");
        Assert.SequenceEqual(Hashes(seq), Hashes(many));

        var before = CopyAll(nm);
        foreach (var (s, e, text) in new (int[], int[], string)[]
        {
            (new[] { 1, 1 }, new[] { 0, 0 }, "listed twice"),
            (new[] { 1, 10 }, new[] { 0, 0 }, "slots[1] = 10 outside"),
            (new[] { 1 }, new[] { 99 }, "entries[0] = 99"),
            (new[] { 1, 2 }, new[] { 0 }, "entries has 1"),
        })
        {
            try
            {
                many.RestoreMany(s, e, nm.Table);
                Assert.Fail($"restore_many({string.Join(",", s)} / {string.Join(",", e)}) did not throw");
            }
            catch (ArgumentException ex)
            {
                Assert.True(ex.Message.Contains(text, StringComparison.Ordinal), $"'{text}' not in: {ex.Message}");
            }
        }
        AssertBuffersEqual(nm, Inline(before), "failed restore_many changes nothing");

        delegate* unmanaged<long, int*, int*, int, void**, int> restoreMany = &Exports.RestoreMany;
        Assert.Equal(-1, restoreMany(987654321, null, null, 0, nm.Table), "invalid handle");
    }

    /// <summary>
    /// The concurrency claim branch and restore_many rely on: RunSim.Clone / CloneDeterminized only read their source,
    /// so 64 clones taken on 4 threads at once equal serial clones, and the source is unchanged (combat, prompts, screens).
    /// </summary>
    public static void Test_VecBranch_ParallelClonesOfOneSourceAreIdentical()
    {
        int states = 0, prompts = 0;
        var hashes = new ulong[64];
        for (int r = 0; r < 6; r++)
        {
            var rng = new System.Random(100 + r);
            var sim = RunSim.NewRun(new RunConfig($"PCL-{r}", AllCharacters[r % AllCharacters.Length], r % 2 == 0 ? 10 : 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < 400 && !sim.IsTerminal; step++)
            {
                if (step % 9 == 0)
                {
                    ulong h = sim.StateHash();
                    ulong exact = sim.Clone().StateHash();
                    ulong det = sim.CloneDeterminized(77).StateHash();
                    Assert.Equal(h, exact);
                    Parallel.For(0, hashes.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                        i => hashes[i] = (i % 2 == 0 ? sim.Clone() : sim.CloneDeterminized(77)).StateHash());
                    for (int i = 0; i < hashes.Length; i++)
                    {
                        Assert.Equal(i % 2 == 0 ? exact : det, hashes[i], $"run {r} step {step}: parallel clone {i}");
                    }
                    Assert.Equal(h, sim.StateHash(), $"run {r} step {step}: the source is unchanged");
                    states++;
                    prompts += sim.HasPendingChoice ? 1 : 0;
                }
                var legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                if (sim.State.Players[0] is { } p && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1 && !sim.HasPendingChoice)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 200);
                    p.CurrentHp = p.MaxHp;
                }
                try
                {
                    sim.Step(legal[rng.Next(legal.Count)]);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        Assert.True(states >= 150 && prompts >= 3, $"states {states}, prompts {prompts}");
    }

    /// <summary>
    /// Memory per branched slot: a destination holds one RunSim clone (until the slot is branched, reset or restored again),
    /// so branching keeps memory bounded by the slot count. Measures the bytes one Clone() / CloneDeterminized() allocates
    /// (GC.GetAllocatedBytesForCurrentThread, exact; an upper bound of what the clone keeps alive, since a clone keeps
    /// nearly everything it allocates and shares immutable data such as the act map) over 90+ states of six healed random
    /// runs. Informational numbers for sim/VEC.md "Search"; sanity bounds only. (A GC.GetTotalMemory delta is not used: on
    /// this runtime it reports about twice the size of a known object graph.)
    /// </summary>
    public static void Test_VecBranch_MemoryPerBranchedSlot()
    {
        var exact = new List<double>();
        var det = new List<double>();
        var combat = new List<double>();
        for (int r = 0; r < 6; r++)
        {
            var rng = new System.Random(500 + r);
            var sim = RunSim.NewRun(new RunConfig($"MEM-{r}", AllCharacters[r % AllCharacters.Length], 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < 600 && !sim.IsTerminal; step++)
            {
                if (step % 40 == 20)
                {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    var c = sim.Clone();
                    long a1 = GC.GetAllocatedBytesForCurrentThread();
                    var d = sim.CloneDeterminized(5);
                    long a2 = GC.GetAllocatedBytesForCurrentThread();
                    GC.KeepAlive(c);
                    GC.KeepAlive(d);
                    exact.Add((a1 - a0) / 1024.0);
                    det.Add((a2 - a1) / 1024.0);
                    if (sim.State.Combat is { IsInProgress: true })
                    {
                        combat.Add((a1 - a0) / 1024.0);
                    }
                }
                var legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                if (sim.State.Players[0] is { } p && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1 && !sim.HasPendingChoice)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 200);
                    p.CurrentHp = p.MaxHp;
                }
                try
                {
                    sim.Step(legal[rng.Next(legal.Count)]);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        Assert.True(exact.Count >= 40 && combat.Count >= 10, $"{exact.Count} samples, {combat.Count} in combat");
        static double Q(List<double> xs, double q) => xs.OrderBy(x => x).ElementAt((int)Math.Min(xs.Count - 1, Math.Floor(q * xs.Count)));
        Assert.True(exact.All(x => x is > 4 and < 512) && det.All(x => x is > 4 and < 512), string.Join(", ", exact.Select(x => $"{x:F0}")));
        Console.WriteLine($"     clone memory over {exact.Count} states: Clone() allocates median {Q(exact, 0.5):F1} KB (p90 {Q(exact, 0.9):F1}, " +
            $"max {Q(exact, 1):F1}; in combat median {Q(combat, 0.5):F1}), CloneDeterminized() median {Q(det, 0.5):F1} KB (max {Q(det, 1):F1})");
    }

    /// <summary>
    /// Lock semantics: one thread steps slots 0-7 through sts2vec_step while another branches slot 8 into 9-15 through
    /// sts2vec_branch on the same env. The calls serialise (every export holds the env lock): no call fails, the stepped
    /// slots end byte- and hash-identical to an env that ran the same steps without branching, the source never changes
    /// (it only no-ops), and the copies equal it.
    /// </summary>
    public static void Test_VecBranch_ConcurrentWithStepSerialises()
    {
        delegate* unmanaged<long, int, int*, int, int, ulong, void**, int> branch = &Exports.Branch;
        delegate* unmanaged<long, int*, int*, int, int, ulong, void**, int> branchMany = &Exports.BranchMany;
        delegate* unmanaged<long, int*, void**, int> step = &Exports.Step;
        delegate* unmanaged<long, byte*, int*, void**, int> reset = &Exports.Reset;
        bool saved = Exports.RequireServerGC;
        Exports.RequireServerGC = false;
        const string cfg = "{\"num_envs\": 16, \"threads\": 2, \"seed_text\": \"CONC\", \"pool\": {\"fresh\": {\"characters\": [\"SILENT\", \"NECROBINDER\"], \"count\": 8}}}";
        long h = CreateEnv(cfg), href = CreateEnv(cfg);
        try
        {
            Assert.True(h > 0 && href > 0, Exports.GlobalError);
            using var nb = new NativeBuffers(16);
            using var nr = new NativeBuffers(16);
            Assert.Equal(0, reset(h, null, null, nb.Table));
            Assert.Equal(0, reset(href, null, null, nr.Table));
            var env = Exports.EnvOf(h)!;
            ulong source = env.StateHash(8);
            var log = new List<int[]>();
            int stepFailures = 0, branchFailures = 0, branches = 0, stepsDone = 0, overlapped = 0;
            var stepper = new Thread(() =>
            {
                var rng = SlotRngs(16, 3);
                for (int t = 0; t < 250; t++)
                {
                    var acts = new int[16];
                    for (int i = 0; i < 16; i++)
                    {
                        int n = nb.Slot(i).CandN;
                        acts[i] = i < 8 && n > 0 ? NextIndex(ref rng[i], n) : -1;
                    }
                    log.Add(acts);
                    fixed (int* a = acts)
                    {
                        if (step(h, a, nb.Table) != 0) Interlocked.Increment(ref stepFailures);
                    }
                    Interlocked.Increment(ref stepsDone);
                }
            });
            var brancher = new Thread(() =>
            {
                int[] dsts = { 9, 10, 11, 12, 13, 14, 15 };
                int[] srcs = { 8, 8, 8, 8, 8, 8, 8 };
                for (int r = 0; r < 250; r++)
                {
                    int done = Volatile.Read(ref stepsDone);
                    if (done > 0 && done < 250)
                    {
                        overlapped++;
                    }
                    fixed (int* d = dsts)
                    fixed (int* s = srcs)
                    {
                        int rc = r % 3 == 2 ? branchMany(h, s, d, dsts.Length, r % 2, (ulong)r, nb.Table) : branch(h, 8, d, dsts.Length, r % 2, (ulong)r, nb.Table);
                        if (rc != 0) Interlocked.Increment(ref branchFailures);
                    }
                    branches++;
                }
            });
            stepper.Start();
            brancher.Start();
            stepper.Join();
            brancher.Join();
            Assert.Equal(0, stepFailures, Exports.GlobalError);
            Assert.Equal(0, branchFailures, Exports.GlobalError);
            Assert.Equal(250, branches);
            Assert.True(overlapped >= 10, $"only {overlapped} branches were issued while the stepper was running");
            Console.WriteLine($"     concurrent: 250 steps x 250 branches on one env, {overlapped} branches issued mid-run; all serialised, no failure");
            foreach (var acts in log)
            {
                fixed (int* a = acts)
                {
                    Assert.Equal(0, step(href, a, nr.Table));
                }
            }
            var refEnv = Exports.EnvOf(href)!;
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(refEnv.StateHash(i), env.StateHash(i), $"slot {i} stepped exactly as without branching");
                for (int k = 0; k < Buf.Count; k++)
                {
                    Assert.True(nb.Slot(i).Bytes(k).SequenceEqual(nr.Slot(i).Bytes(k)), $"slot {i} buffer {Schema.Buffers[k].Name}");
                }
            }
            Assert.Equal(source, env.StateHash(8), "the source only no-opped");
            int[] all = { 9, 10, 11, 12, 13, 14, 15 };
            fixed (int* d = all)
            {
                Assert.Equal(0, branch(h, 8, d, all.Length, 0, 0, nb.Table));
            }
            foreach (int d in all)
            {
                Assert.Equal(source, env.StateHash(d));
            }
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(0, nb.Slot(i).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap), $"slot {i}");
            }
        }
        finally
        {
            Destroy(h);
            Destroy(href);
            Exports.RequireServerGC = saved;
        }
    }
}
