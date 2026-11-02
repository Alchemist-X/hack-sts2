using System.Diagnostics;
using Sts2Sim.Core;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Bench;

/// <summary>
/// <c>dotnet run -c Release --project sim/Sts2Sim.Bench -- fastpath [runs=200] [ascension=0]</c>: single-threaded
/// RunSim cost per step, cloning path (<c>Step(a)</c>) vs clone-free path (<c>StepFast(a)</c>), on the same uniform-random
/// trajectories (5 characters, ported content). Times only the step call (and, separately, the MayPrompt predicate);
/// NewRun, LegalActions and the policy are outside the clock. Prints the fast-path fraction.
/// </summary>
public static class FastPathBench
{
    private static readonly string[] Characters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    public static int Run(string[] args)
    {
        int Arg(int i, int dflt) => args.Length > i && int.TryParse(args[i], out var v) ? v : dflt;
        int runs = Arg(1, 200);
        int ascension = Arg(2, 0);
        var starts = new RunSim[runs];
        for (int r = 0; r < runs; r++)
        {
            starts[r] = RunSim.NewRun(new RunConfig($"FPBENCH{r:D5}", Characters[r % Characters.Length], ascension) { Content = ContentFilter.PortedOnly });
        }
        Pass(starts, fast: false, out _, out _, out _);
        Pass(starts, fast: true, out _, out _, out _);
        for (int rep = 0; rep < 2; rep++)
        {
            double slowTicks = Pass(starts, fast: false, out long steps, out _, out ulong h1);
            double fastTicks = Pass(starts, fast: true, out long steps2, out long fastSteps, out ulong h2);
            double predTicks = PredicateCost(starts);
            double slowUs = slowTicks / Stopwatch.Frequency * 1e6 / steps;
            double fastUs = fastTicks / Stopwatch.Frequency * 1e6 / steps2;
            double predUs = predTicks / Stopwatch.Frequency * 1e6 / steps;
            Console.WriteLine($"fastpath: A{ascension} runs={runs} steps={steps} fast fraction={(double)fastSteps / steps2:F3} " +
                $"step µs: cloning={slowUs:F2} fast-path={fastUs:F2} (incl. MayPrompt {predUs:F2}) speedup={slowUs / fastUs:F2}x " +
                $"final-hash-xor {(h1 == h2 ? "equal" : "DIFFERENT")}");
            if (h1 != h2 || steps != steps2)
            {
                return 1;
            }
        }
        return 0;
    }

    private static double Pass(RunSim[] starts, bool fast, out long steps, out long fastSteps, out ulong hashXor)
    {
        long ticks = 0;
        steps = 0;
        fastSteps = 0;
        hashXor = 0;
        var legal = new List<RunAction>(64);
        for (int r = 0; r < starts.Length; r++)
        {
            var sim = starts[r].Clone();
            var rng = new System.Random(r);
            for (int n = 0; n < 3000 && !sim.IsTerminal; n++)
            {
                sim.LegalActions(legal);
                var a = legal[rng.Next(legal.Count)];
                long t0 = Stopwatch.GetTimestamp();
                if (fast)
                {
                    if (sim.StepFast(a))
                    {
                        fastSteps++;
                    }
                }
                else
                {
                    sim.Step(a);
                }
                ticks += Stopwatch.GetTimestamp() - t0;
                steps++;
            }
            hashXor ^= sim.StateHash();
        }
        return ticks;
    }

    private static double PredicateCost(RunSim[] starts)
    {
        long ticks = 0;
        var legal = new List<RunAction>(64);
        bool sink = false;
        for (int r = 0; r < starts.Length; r++)
        {
            var sim = starts[r].Clone();
            var rng = new System.Random(r);
            for (int n = 0; n < 3000 && !sim.IsTerminal; n++)
            {
                sim.LegalActions(legal);
                var a = legal[rng.Next(legal.Count)];
                long t0 = Stopwatch.GetTimestamp();
                sink ^= sim.MayPrompt(a);
                ticks += Stopwatch.GetTimestamp() - t0;
                sim.StepFast(a);
            }
        }
        GC.KeepAlive(sink);
        return ticks;
    }
}
