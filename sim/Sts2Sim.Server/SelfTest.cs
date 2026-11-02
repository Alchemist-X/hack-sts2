using System.Diagnostics;
using System.Globalization;

namespace Sts2Sim.Server;

public sealed record SelfTestOptions
{
    public int Runs { get; init; } = 10;

    public int FirstSeed { get; init; } = 1;

    public int StepCap { get; init; } = 6_000;

    public IReadOnlyList<string>? Characters { get; init; }

    public int Ascension { get; init; }

    public string Content { get; init; } = "ported_only";

    // "random": uniform over legal tokens. "biased": prefers card plays over ending the turn and
    // taking things over leaving/skipping, so runs reach deeper floors.
    public string Policy { get; init; } = "random";
}

public sealed record SelfTestResult(int Runs, int Wins, int Losses, int Crashed, int Capped, int ServerErrors, long Steps, IReadOnlyDictionary<string, int> CrashSignatures, IReadOnlyList<string> ServerErrorMessages)
{
    public int Completed => Wins + Losses;

    public double MeanSteps => Runs == 0 ? 0 : (double)Steps / Runs;
}

// Random-policy playtest through PlayService.Handle (the HTTP handlers without sockets).
public static class SelfTest
{
    public static string SeedText(int seed) => "SELF" + seed.ToString("D8", CultureInfo.InvariantCulture);

    public static SelfTestResult Run(PlayService service, SelfTestOptions options)
    {
        var characters = options.Characters is { Count: > 0 } c ? c : CharacterRegistry.AllIds;
        int wins = 0, losses = 0, crashed = 0, capped = 0, serverErrors = 0;
        long steps = 0;
        var signatures = new Dictionary<string, int>(StringComparer.Ordinal);
        var serverMessages = new List<string>();
        for (int r = 0; r < options.Runs; r++)
        {
            int seed = options.FirstSeed + r;
            var random = new System.Random(seed);
            var body = new JsonObject
            {
                ["seed"] = SeedText(seed),
                ["character"] = characters[r % characters.Count],
                ["ascension"] = options.Ascension,
                ["content"] = options.Content
            };
            var created = service.Handle("POST", "/runs", body.ToJsonString());
            if (created.Status == 500)
            {
                crashed++;
                Count(signatures, created.Body["error"]?.GetValue<string>());
                continue;
            }
            if (created.Status != 200)
            {
                serverErrors++;
                serverMessages.Add($"create {SeedText(seed)}: {created.Json}");
                continue;
            }
            string id = created.Body["id"]!.GetValue<string>();
            var obs = created.Body["observation"]!.AsObject();
            int n = 0;
            while (true)
            {
                if (obs["terminal"]!.GetValue<bool>())
                {
                    if (obs["result"]?.GetValue<string>() == "win")
                    {
                        wins++;
                    }
                    else
                    {
                        losses++;
                    }
                    break;
                }
                if (n >= options.StepCap)
                {
                    capped++;
                    break;
                }
                var legal = obs["legal_actions"]!.AsArray();
                if (legal.Count == 0)
                {
                    serverErrors++;
                    serverMessages.Add($"{id} {SeedText(seed)}: no legal actions in a non-terminal phase {obs["phase"]}");
                    break;
                }
                string token = Choose(legal, random, options.Policy == "biased");
                var step = service.Handle("POST", $"/games/{id}/step", new JsonObject { ["token"] = token }.ToJsonString());
                n++;
                if (step.Status == 500)
                {
                    crashed++;
                    Count(signatures, step.Body["error"]?.GetValue<string>());
                    break;
                }
                if (step.Status != 200)
                {
                    serverErrors++;
                    serverMessages.Add($"{id} {SeedText(seed)} step {n} '{token}': HTTP {step.Status} {step.Body["error"]}");
                    break;
                }
                obs = step.Body["observation"]!.AsObject();
            }
            steps += n;
            service.Handle("DELETE", $"/games/{id}", null);
        }
        return new SelfTestResult(options.Runs, wins, losses, crashed, capped, serverErrors, steps, signatures, serverMessages);
    }

    public static string Choose(JsonArray legal, System.Random random, bool biased)
    {
        var tokens = legal.Select(a => a!["token"]!.GetValue<string>()).ToList();
        if (!biased)
        {
            return tokens[random.Next(tokens.Count)];
        }
        var plays = tokens.Where(t => t.StartsWith("play ", StringComparison.Ordinal)).ToList();
        if (plays.Count > 0 && random.Next(10) != 0)
        {
            return plays[random.Next(plays.Count)];
        }
        var active = tokens.Where(t => t is not ("end" or "proceed" or "leave" or "skip" or "cancel") && !t.StartsWith("discard ", StringComparison.Ordinal)).ToList();
        if (active.Count > 0 && random.Next(10) != 0)
        {
            return active[random.Next(active.Count)];
        }
        return tokens[random.Next(tokens.Count)];
    }

    private static void Count(Dictionary<string, int> signatures, string? error)
    {
        string key = error ?? "?";
        if (key.Length > 160)
        {
            key = key[..160];
        }
        signatures[key] = signatures.GetValueOrDefault(key) + 1;
    }

    public static int Cli(PlayService service, SelfTestOptions options)
    {
        var sw = Stopwatch.StartNew();
        var result = Run(service, options);
        sw.Stop();
        Console.WriteLine($"selftest ({options.Policy}): {result.Runs} runs, completed {result.Completed} (wins {result.Wins}, losses {result.Losses}), crashed {result.Crashed}, capped {result.Capped}, server errors {result.ServerErrors}, mean steps {result.MeanSteps:F1}, {sw.Elapsed.TotalSeconds:F1}s");
        foreach (var (sig, count) in result.CrashSignatures.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  crash x{count}: {sig}");
        }
        foreach (var msg in result.ServerErrorMessages.Take(20))
        {
            Console.WriteLine($"  server error: {msg}");
        }
        if (result.Crashed > 0)
        {
            Console.WriteLine($"  crash repros appended to {service.Options.CrashLogPath ?? "(crash log off)"}");
        }
        return result.ServerErrors == 0 && result.Crashed == 0 ? 0 : 1;
    }
}
