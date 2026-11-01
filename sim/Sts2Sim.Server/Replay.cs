namespace Sts2Sim.Server;

// Re-runs one crash-log repro (config + token history) in process and reports where it fails.
public static class Replay
{
    public sealed record ReplayResult(bool Reproduced, int StepsApplied, string? Error, string? Stack);

    public static ReplayResult Run(JsonObject repro)
    {
        string kind = repro["kind"]?.GetValue<string>() ?? "run";
        var config = repro["config"]!.AsObject();
        var tokens = repro["tokens"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
        GameSession g;
        try
        {
            g = PlayService.Instantiate("replay", kind == "combat" ? GameKind.Combat : GameKind.Run, config);
        }
        catch (Exception ex)
        {
            return new ReplayResult(true, 0, $"{ex.GetType().Name}: {ex.Message}", ex.StackTrace);
        }
        for (int i = 0; i < tokens.Count; i++)
        {
            try
            {
                var legal = ActionTokens.Enumerate(g);
                if (!ActionTokens.TryResolve(legal, tokens[i], out var entry, out var error))
                {
                    return new ReplayResult(false, i, $"token {i} '{tokens[i]}' is not legal on replay: {error}", null);
                }
                g.Step(entry.Action);
                ObservationBuilder.Build(g, ActionTokens.Enumerate(g));
            }
            catch (Exception ex)
            {
                return new ReplayResult(true, i, $"{ex.GetType().Name}: {ex.Message}", ex.StackTrace);
            }
        }
        return new ReplayResult(false, tokens.Count, null, null);
    }

    public static int Cli(string path, int? line)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0)
        {
            Console.Error.WriteLine($"{path} is empty");
            return 2;
        }
        int index = line is { } n ? n - 1 : lines.Length - 1;
        if (index < 0 || index >= lines.Length)
        {
            Console.Error.WriteLine($"line {line} is outside 1..{lines.Length}");
            return 2;
        }
        var repro = JsonNode.Parse(lines[index])!.AsObject();
        Console.WriteLine($"replaying {path}:{index + 1}: {repro["kind"]} config {repro["config"]!.ToJsonString(PlayService.JsonOptions)}, {repro["tokens"]!.AsArray().Count} tokens; logged error: {repro["error"]}");
        var result = Run(repro);
        if (result.Reproduced)
        {
            Console.WriteLine($"REPRODUCED after {result.StepsApplied} applied tokens: {result.Error}");
            Console.WriteLine(result.Stack);
            return 1;
        }
        Console.WriteLine(result.Error != null ? $"NOT reproduced: {result.Error}" : $"NOT reproduced: all {result.StepsApplied} tokens applied cleanly");
        return 0;
    }
}
