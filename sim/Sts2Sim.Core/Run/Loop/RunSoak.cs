namespace Sts2Sim.Core.Run;

public sealed record RunSoakOptions
{
    public int Runs { get; init; } = 10_000;

    public int FirstSeed { get; init; } = 1;

    public int StepCap { get; init; } = 6_000;

    public string CharacterId { get; init; } = StartingDecks.IroncladCharacterId;

    public IReadOnlyList<string>? CharacterIds { get; init; }

    public bool PropagateNotImplemented { get; init; }

    public int Ascension { get; init; }

    public ContentFilter Content { get; init; } = ContentFilter.PortedOnly;

    public int HashCheckEvery { get; init; } = 101;

    public int ClosureCheckEvery { get; init; } = 499;

    public int MaxListedFailures { get; init; } = 30;
}

public sealed record RunSoakResult(int Runs, int Victories, int Defeats, int Capped, long Steps, int FailureCount, IReadOnlyList<string> Failures)
{
    public IReadOnlyDictionary<string, int> FailureSignatures { get; init; } = new Dictionary<string, int>();

    public int ContentGapCount { get; init; }

    public IReadOnlyDictionary<string, int> ContentGaps { get; init; } = new Dictionary<string, int>();
}

public static class RunSoak
{
    public const string RunLayerNamespace = "Sts2Sim.Core.Run";

    public static string SeedText(int seed) => "SOAK" + seed.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsRunLayerStub(NotImplementedException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var ns = ex.TargetSite?.DeclaringType?.Namespace;
        return ns != null && (ns == RunLayerNamespace || ns.StartsWith(RunLayerNamespace + ".", StringComparison.Ordinal));
    }

    public static RunSoakResult Run(RunSoakOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var signatures = new Dictionary<string, int>(StringComparer.Ordinal);
        var gaps = new Dictionary<string, int>(StringComparer.Ordinal);
        var characters = options.CharacterIds is { Count: > 0 } ids ? ids : new[] { options.CharacterId };
        int failureCount = 0;
        int gapCount = 0;
        int victories = 0;
        int defeats = 0;
        int capped = 0;
        long steps = 0;
        var buffer = new List<RunAction>(64);
        var probe = new List<RunAction>(64);
        for (int r = 0; r < options.Runs; r++)
        {
            int seed = options.FirstSeed + r;
            var config = new RunConfig(SeedText(seed), characters[r % characters.Count], options.Ascension) { Content = options.Content };
            var random = new System.Random(seed);
            int n = 0;
            RunPhase phase = RunPhase.Map;
            try
            {
                var sim = RunSim.NewRun(config);
                while (!sim.IsTerminal && n < options.StepCap)
                {
                    phase = sim.Phase;
                    sim.LegalActions(buffer);
                    if (buffer.Count == 0)
                    {
                        throw new InvalidOperationException($"no legal action in phase {phase} at step {n}");
                    }
                    if (options.HashCheckEvery > 0 && n % options.HashCheckEvery == 0)
                    {
                        var clone = sim.Clone();
                        if (clone.StateHash() != sim.StateHash())
                        {
                            throw new InvalidOperationException($"Hash(x) != Hash(x.Clone()) in phase {phase} at step {n}");
                        }
                    }
                    if (options.ClosureCheckEvery > 0 && n % options.ClosureCheckEvery == 0)
                    {
                        CheckClosure(sim, buffer, probe, phase, n);
                    }
                    var action = buffer[random.Next(buffer.Count)];
                    if (!sim.IsLegal(action))
                    {
                        throw new InvalidOperationException($"listed action {action} is not legal in phase {phase} at step {n}");
                    }
                    sim.Step(action);
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
            catch (NotImplementedException) when (options.PropagateNotImplemented)
            {
                throw;
            }
            catch (NotImplementedException ex) when (!IsRunLayerStub(ex))
            {
                gapCount++;
                gaps[ex.Message] = gaps.TryGetValue(ex.Message, out int seen) ? seen + 1 : 1;
            }
            catch (Exception ex)
            {
                failureCount++;
                string signature = ex.GetType().Name + ": " + SignatureOf(ex.Message);
                signatures[signature] = signatures.TryGetValue(signature, out int seen) ? seen + 1 : 1;
                if (failures.Count < options.MaxListedFailures)
                {
                    failures.Add($"{config.SeedText}/{config.CharacterId}/{phase}/{n}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            steps += n;
        }
        return new RunSoakResult(options.Runs, victories, defeats, capped, steps, failureCount, failures)
        {
            FailureSignatures = signatures,
            ContentGapCount = gapCount,
            ContentGaps = gaps
        };
    }

    private static string SignatureOf(string message)
        => System.Text.RegularExpressions.Regex.Replace(message, @"(legal action \S+( \S+)?|at step \d+)", "");

    private static void CheckClosure(RunSim sim, List<RunAction> legal, List<RunAction> probe, RunPhase phase, int step)
    {
        var snapshot = legal.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            var clone = sim.Clone();
            try
            {
                clone.Step(snapshot[i]);
                if (!clone.IsTerminal)
                {
                    clone.LegalActions(probe);
                }
            }
            catch (NotImplementedException ex) when (!IsRunLayerStub(ex))
            {
            }
            catch (Exception ex) when (ex is not NotImplementedException)
            {
                throw new InvalidOperationException($"closure: legal action {snapshot[i]} in phase {phase} at step {step} failed: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }
    }
}
