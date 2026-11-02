using Sts2Sim.Core.Choice;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>Vec clone-free stepping (config fast_path; RunSim.StepFast): same outputs as the cloning path, fail-closed on a prompt.</summary>
public static unsafe partial class VecTests
{
    /// <summary>fast_path true and false give byte-identical buffers and identical StateHash per slot over 300 random batches.</summary>
    public static void Test_Vec_FastPathMatchesCloningPath()
    {
        const int N = 32;
        foreach (int asc in new[] { 0, 10 })
        {
            var cfg = new VecConfig(N, Threads: 4, SeedText: "FPVEC", Pool: new FreshPoolSpec(AllCharacters, Ascension: asc, Count: 16, SeedPrefix: "FP"));
            var fast = new VecEnv(cfg);
            var slow = new VecEnv(cfg with { FastPath = false });
            using var nf = new NativeBuffers(N);
            using var ns = new NativeBuffers(N);
            fast.Reset(default, default, nf.Table);
            slow.Reset(default, default, ns.Table);
            AssertBuffersEqual(nf, ns, $"A{asc} reset");
            var rng = SlotRngs(N, 77);
            var actions = new int[N];
            for (int t = 0; t < 300; t++)
            {
                RandomActions(nf, rng, actions);
                fast.Step(actions, nf.Table);
                slow.Step(actions, ns.Table);
                AssertBuffersEqual(nf, ns, $"A{asc} batch {t}");
                Assert.SequenceEqual(Hashes(slow), Hashes(fast), $"A{asc} batch {t} state hashes");
            }
            Assert.Equal(0L, slow.FastSteps, "fast_path false never takes the clone-free path");
            Assert.True(fast.FastSteps > fast.SlowSteps, $"A{asc}: fast {fast.FastSteps} vs slow {fast.SlowSteps} steps");
            Assert.True(fast.Slots.All(s => s.LastError == null || !s.LastError.Contains("Fast-path", StringComparison.Ordinal)), "no fast-path prompt");
            Console.WriteLine($"     Vec fast path A{asc}: {fast.FastSteps} clone-free / {fast.SlowSteps} cloning steps, buffers and hashes identical to fast_path=false");
        }
    }

    /// <summary>
    /// A prompt on a forced clone-free step (ForceFastPath) is a SIM_ERROR with the FastPathPromptException message,
    /// ends the episode (truncated), and the slot auto-resets; nothing is answered silently.
    /// </summary>
    public static void Test_Vec_FastPathPromptIsSimErrorNotSilent()
    {
        const int N = 16;
        var env = new VecEnv(new VecConfig(N, Threads: 4, SeedText: "FPFAIL", Pool: new FreshPoolSpec(new[] { "SILENT" }, Count: 8, SeedPrefix: "FPF")));
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        env.ForceFastPath = (_, _) => true;
        var rng = SlotRngs(N, 5);
        var actions = new int[N];
        for (int t = 0; t < 400; t++)
        {
            RandomActions(nb, rng, actions);
            env.Step(actions, nb.Table);
            for (int i = 0; i < N; i++)
            {
                var s = nb.Slot(i);
                var err = env.Slots[i].LastError;
                if (((SlotStatus)s.Status).HasFlag(SlotStatus.SimError) && err != null && err.Contains(nameof(FastPathPromptException), StringComparison.Ordinal))
                {
                    Assert.True(err.Contains("reached choice prompt", StringComparison.Ordinal), err);
                    Assert.Equal(1, (int)s.Truncated);
                    Assert.Equal(0, (int)s.Terminated);
                    Assert.Equal((int)EndKind.SimError, s.LastEpisode[LastEpCol.EndKind]);
                    Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Reset), "the slot auto-resets");
                    Assert.True(s.CandN > 0, "the restarted slot is playable");
                    env.ForceFastPath = null;
                    return;
                }
            }
        }
        Assert.Fail("no forced clone-free step met a prompt in 400 batches of Silent runs");
    }
}
