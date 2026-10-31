using System.Diagnostics;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;

namespace Sts2Sim.Bench;

public static class Program
{
    private const int StepCap = 1000;

    public static int Main(string[] args)
    {
        string encounter = args.Length > 0 ? args[0] : NibbitsWeak.EncounterIdEntry;
        double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 5.0;
        var deck = StartingDecks.Ironclad;

        Console.WriteLine($"encounter={encounter} deck=Ironclad starter ({deck.Count} cards) runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.ToString()} arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

        var mid = MidFight(deck, encounter, out int midSteps);
        Console.WriteLine($"mid-fight state: {midSteps} actions in, hand={mid.State.Player.Hand.Count}");

        RunPlayouts(deck, encounter, 1.0, quiet: true);
        RunCloneBench(mid, 2_000, quiet: true);
        RunHashBench(mid, 2_000, quiet: true);

        RunCloneBench(mid, 100_000, quiet: false);
        RunHashBench(mid, 200_000, quiet: false);
        RunPlayouts(deck, encounter, seconds, quiet: false);
        return 0;
    }

    private static CombatSim MidFight(IReadOnlyList<string> deck, string encounter, out int steps)
    {
        var rng = new System.Random(777);
        var buf = new List<GameAction>(32);
        for (ulong seed = 42; ; seed++)
        {
            var sim = CombatSim.Create(seed, deck, encounter);
            int n = 0;
            while (!sim.IsTerminal && n < 6)
            {
                sim.LegalActions(buf);
                var plays = buf.Where(a => a.Kind == ActionKind.PlayCard).ToList();
                var pick = n == 3 || plays.Count == 0 ? GameAction.EndTurn : plays[rng.Next(plays.Count)];
                if (!sim.IsLegal(pick)) pick = buf[rng.Next(buf.Count)];
                sim.Step(pick);
                n++;
            }
            if (!sim.IsTerminal) { steps = n; return sim; }
        }
    }

    private static void RunCloneBench(CombatSim sim, int n, bool quiet)
    {
        ulong sink = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            var c = sim.Clone();
            sink += c.StateHash();
        }
        sw.Stop();
        double hashUs = HashUs(sim, 20_000);
        double total = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
        if (!quiet)
            Console.WriteLine($"clone: n={n} clone+hash={total:F3} us, hash-only={hashUs:F3} us => clone ~{total - hashUs:F3} us/clone (sink={sink:X})");
    }

    private static double HashUs(CombatSim sim, int n)
    {
        ulong sink = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) sink ^= sim.StateHash() + (ulong)i;
        sw.Stop();
        GC.KeepAlive(sink);
        return sw.Elapsed.TotalMilliseconds * 1000.0 / n;
    }

    private static void RunHashBench(CombatSim sim, int n, bool quiet)
    {
        double us = HashUs(sim, n);
        if (!quiet) Console.WriteLine($"statehash: n={n} => {us:F3} us/call");
    }

    private static void RunPlayouts(IReadOnlyList<string> deck, string encounter, double seconds, bool quiet)
    {
        var policy = new System.Random(12345);
        var buf = new List<GameAction>(32);
        long decisions = 0, completed = 0, capped = 0, wins = 0, episodes = 0;
        ulong seed = 1;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var sim = CombatSim.Create(seed++, deck, encounter);
            int steps = 0;
            while (!sim.IsTerminal && steps < StepCap)
            {
                sim.LegalActions(buf);
                sim.Step(buf[policy.Next(buf.Count)]);
                steps++;
            }
            decisions += steps;
            episodes++;
            if (sim.IsTerminal) { completed++; if (sim.Outcome == CombatOutcome.Victory) wins++; } else capped++;
        }
        sw.Stop();
        if (!quiet)
            Console.WriteLine($"playouts: {decisions} decisions, {episodes} episodes ({completed} completed, {capped} capped, {wins} wins) in {sw.Elapsed.TotalSeconds:F2}s => {decisions / sw.Elapsed.TotalSeconds:F0} decisions/s, {episodes / sw.Elapsed.TotalSeconds:F0} episodes/s, {(double)decisions / episodes:F1} decisions/episode");
    }
}
