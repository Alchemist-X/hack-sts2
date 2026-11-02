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
        if (args.Length > 0 && args[0] == "run-soak")
        {
            return RunSoakBench(args);
        }
        if (args.Length > 0 && args[0] == "vec")
        {
            return VecBench.Run(args);
        }
        if (args.Length > 0 && args[0] == "fastpath")
        {
            return FastPathBench.Run(args);
        }
        if (args.Length > 0 && args[0] == "nightly-soak")
        {
            return NightlySoak.Run(args);
        }
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
    private static int RunSoakBench(string[] args)
    {
        int runs = args.Length > 1 && int.TryParse(args[1], out var r) ? r : 100_000;
        int firstSeed = args.Length > 2 && int.TryParse(args[2], out var f) ? f : 1;
        var sw = Stopwatch.StartNew();
        Sts2Sim.Core.Run.RunSoakResult result;
        try
        {
            result = Sts2Sim.Core.Run.RunSoak.Run(new Sts2Sim.Core.Run.RunSoakOptions { Runs = runs, FirstSeed = firstSeed });
        }
        catch (NotImplementedException ex)
        {
            Console.WriteLine($"run-soak: blocked on a W2 stub: {ex.Message}");
            return 2;
        }
        sw.Stop();
        Console.WriteLine($"run-soak: {result.Runs} runs ({result.Victories} victories, {result.Defeats} defeats, {result.Capped} capped), {result.Steps} steps in {sw.Elapsed.TotalSeconds:F1}s, {result.FailureCount} failures");
        foreach (var failure in result.Failures)
        {
            Console.WriteLine("  " + failure);
        }
        return result.FailureCount == 0 ? 0 : 1;
    }
}

public static class NightlySoak
{
    private static readonly int[] Ascensions = { 0, 5, 10 };

    public static int Run(string[] args)
    {
        int runsPerCell = args.Length > 1 && int.TryParse(args[1], out var r) ? r : 200;
        int firstSeed = args.Length > 2 && int.TryParse(args[2], out var f) ? f : 1;
        int stepCap = args.Length > 3 && int.TryParse(args[3], out var c) ? c : 6000;
        var characters = Sts2Sim.Core.Registry.CharacterRegistry.AllIds.Count > 0
            ? Sts2Sim.Core.Registry.CharacterRegistry.AllIds.OrderBy(id => id, StringComparer.Ordinal).ToList()
            : new List<string> { StartingDecks.IroncladCharacterId };
        var signatures = new Dictionary<string, (int Count, string Example, bool Gap)>(StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();
        var buffer = new List<RunAction>(64);
        long totalSteps = 0;
        foreach (var character in characters)
        {
            foreach (var ascension in Ascensions)
            {
                int victories = 0, defeats = 0, capped = 0, failed = 0;
                for (int i = 0; i < runsPerCell; i++)
                {
                    int seed = firstSeed + i;
                    var config = new Sts2Sim.Core.Run.RunConfig("NIGHTLY" + seed.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), character, ascension)
                    {
                        Content = Sts2Sim.Core.Run.ContentFilter.PortedOnly
                    };
                    var random = new System.Random(seed * 31 + ascension);
                    var phase = RunPhase.Map;
                    int n = 0;
                    try
                    {
                        var sim = Sts2Sim.Core.Run.RunSim.NewRun(config);
                        while (!sim.IsTerminal && n < stepCap)
                        {
                            phase = sim.Phase;
                            sim.LegalActions(buffer);
                            if (buffer.Count == 0)
                            {
                                throw new InvalidOperationException($"no legal action in phase {phase}");
                            }
                            if (n % 101 == 0 && sim.Clone().StateHash() != sim.StateHash())
                            {
                                throw new InvalidOperationException($"Hash(x) != Hash(x.Clone()) in phase {phase}");
                            }
                            sim.Step(buffer[random.Next(buffer.Count)]);
                            n++;
                        }
                        if (!sim.IsTerminal)
                        {
                            capped++;
                        }
                        else if (sim.Phase == RunPhase.Victory)
                        {
                            victories++;
                        }
                        else
                        {
                            defeats++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        string signature = $"{ex.GetType().Name}: {System.Text.RegularExpressions.Regex.Replace(ex.Message, @"\d+", "#")}";
                        string example = $"{character}/A{ascension}/{config.SeedText}/{phase}/{n}";
                        signatures[signature] = signatures.TryGetValue(signature, out var known)
                            ? (known.Count + 1, known.Example, known.Gap)
                            : (1, example, ex is NotImplementedException or NotSupportedException);
                    }
                    totalSteps += n;
                }
                Console.WriteLine($"nightly-soak {character} A{ascension}: {runsPerCell} runs ({victories} victories, {defeats} defeats, {capped} capped, {failed} failed)");
            }
        }
        sw.Stop();
        int bugs = signatures.Values.Where(v => !v.Gap).Sum(v => v.Count);
        int gaps = signatures.Values.Where(v => v.Gap).Sum(v => v.Count);
        Console.WriteLine($"nightly-soak: {characters.Count} characters x {Ascensions.Length} ascensions x {runsPerCell} runs, {totalSteps} steps in {sw.Elapsed.TotalSeconds:F1}s; {signatures.Count} distinct exception signatures ({bugs} runs failed, {gaps} runs stopped at unported content)");
        foreach (var (signature, info) in signatures.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {(info.Gap ? "gap" : "bug")} x{info.Count} {signature}  (first: {info.Example})");
        }
        return bugs == 0 ? 0 : 1;
    }
}
