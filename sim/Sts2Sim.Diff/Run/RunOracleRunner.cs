using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Run;

public sealed record RunOracleFileResult(string Path, RunRecordKind? Kind, IReadOnlyList<RunOracleCheck> Checks, string? IgnoredReason = null);

public static class RunOracleRunner
{
    public const string Usage = "usage: Sts2Sim.Diff run <dir-or-file> [--report out.json] [--any-build] [--strict] [-v|--verbose]";

    private const int MaxPrinted = 20;

    public static IReadOnlyList<RunOracleCheck> CheckRecord(RunRecord record, RunOracleOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);
        var reasons = record.UnsupportedReasons().ToList();
        if (options.AnyBuild)
        {
            reasons.RemoveAll(r => r.StartsWith("build ", StringComparison.Ordinal));
        }
        var checks = new List<RunOracleCheck>();
        if (reasons.Count > 0)
        {
            string reason = string.Join("; ", reasons);
            foreach (var name in OraclesFor(record.Kind))
            {
                checks.Add(RunOracleCheck.Unsupported(name, record.Source, reason));
            }
            return checks;
        }
        if (record.Kind == RunRecordKind.Save)
        {
            checks.Add(RunStartOracle.Check(record, options));
        }
        checks.Add(NeowOracle.Check(record, options));
        if (record.Kind == RunRecordKind.Save)
        {
            checks.AddRange(MapOracle.Check(record, options));
        }
        checks.Add(UnknownRollOracle.Check(record, options));
        checks.AddRange(RoomPullOracle.Check(record, options));
        return checks;
    }

    public static IEnumerable<string> OraclesFor(RunRecordKind kind) => kind == RunRecordKind.Save
        ? RunOracleNames.All
        : new[] { RunOracleNames.Neow, RunOracleNames.UnknownRolls, RunOracleNames.EncounterPulls, RunOracleNames.EventPulls };

    public static RunOracleFileResult CheckFile(string path, RunOracleOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RunOracleFileResult(path, null, new[] { RunOracleCheck.Error("read", path, ex) });
        }
        RunRecord record;
        try
        {
            using (var doc = JsonDocument.Parse(text))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out _) || !root.TryGetProperty("players", out _))
                {
                    return new RunOracleFileResult(path, null, Array.Empty<RunOracleCheck>(), "not a run save or run history");
                }
                if (!root.TryGetProperty("rng", out _) && !root.TryGetProperty("seed", out _))
                {
                    return new RunOracleFileResult(path, null, Array.Empty<RunOracleCheck>(), "no seed");
                }
            }
            record = path.EndsWith(".run", StringComparison.OrdinalIgnoreCase)
                ? RunRecordReader.ReadHistory(text, path)
                : RunRecordReader.FromSave(RunSaveReader.Read(text), path);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return new RunOracleFileResult(path, null, new[] { RunOracleCheck.Error("parse", path, ex) });
        }
        return new RunOracleFileResult(path, record.Kind, CheckRecord(record, options));
    }

    public static List<string> Discover(string input)
    {
        string full = Path.GetFullPath(input);
        if (File.Exists(full))
        {
            return new List<string> { full };
        }
        if (!Directory.Exists(full))
        {
            throw new IOException($"{input} is neither a file nor a directory");
        }
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".run", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static Dictionary<string, Dictionary<RunOracleStatus, int>> Tally(IEnumerable<RunOracleCheck> checks)
    {
        var tally = new Dictionary<string, Dictionary<RunOracleStatus, int>>(StringComparer.Ordinal);
        foreach (var c in checks)
        {
            if (!tally.TryGetValue(c.Oracle, out var row))
            {
                row = Enum.GetValues<RunOracleStatus>().ToDictionary(s => s, _ => 0);
                tally[c.Oracle] = row;
            }
            row[c.Status]++;
        }
        return tally;
    }

    public static JsonObject BuildReport(IReadOnlyList<RunOracleFileResult> results, string input)
    {
        var all = results.SelectMany(r => r.Checks).ToList();
        var summary = new JsonObject();
        foreach (var (oracle, row) in Tally(all).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var o = new JsonObject();
            foreach (var (status, count) in row)
            {
                o[status.ToString().ToLowerInvariant()] = count;
            }
            summary[oracle] = o;
        }
        var checks = new JsonArray();
        foreach (var c in all)
        {
            checks.Add(new JsonObject
            {
                ["oracle"] = c.Oracle,
                ["source"] = c.Source,
                ["status"] = c.Status.ToString().ToLowerInvariant(),
                ["compared"] = c.Compared,
                ["detail"] = c.Detail
            });
        }
        var ignored = new JsonArray();
        foreach (var r in results.Where(r => r.IgnoredReason != null))
        {
            ignored.Add(new JsonObject { ["path"] = r.Path, ["reason"] = r.IgnoredReason });
        }
        return new JsonObject
        {
            ["run_oracle_report_version"] = 1,
            ["input"] = Path.GetFullPath(input),
            ["files"] = results.Count(r => r.IgnoredReason == null),
            ["summary"] = summary,
            ["checks"] = checks,
            ["ignored"] = ignored
        };
    }

    public static int RunMain(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? input = null;
        string? reportPath = null;
        bool verbose = false;
        bool strict = false;
        bool anyBuild = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--report" && i + 1 < args.Length)
            {
                reportPath = args[++i];
            }
            else if (arg is "-v" or "--verbose")
            {
                verbose = true;
            }
            else if (arg == "--strict")
            {
                strict = true;
            }
            else if (arg == "--any-build")
            {
                anyBuild = true;
            }
            else if (arg is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return 0;
            }
            else if (input == null && !arg.StartsWith('-'))
            {
                input = arg;
            }
            else
            {
                Console.Error.WriteLine($"unexpected argument '{arg}'");
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }
        if (input == null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        List<string> files;
        try
        {
            files = Discover(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"cannot read {input}: {ex.Message}");
            return 2;
        }
        var options = new RunOracleOptions { AnyBuild = anyBuild };
        var results = files.Select(f => CheckFile(f, options)).ToList();
        int used = results.Count(r => r.IgnoredReason == null);
        if (used == 0)
        {
            Console.Error.WriteLine($"no run saves or .run files under {input}");
            return 2;
        }
        var all = results.SelectMany(r => r.Checks).ToList();
        Console.WriteLine($"run oracles: {used} files ({results.Count(r => r.Kind == RunRecordKind.Save)} saves, {results.Count(r => r.Kind == RunRecordKind.History)} .run)");
        foreach (var (oracle, row) in Tally(all).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {oracle}: agree {row[RunOracleStatus.Agree]}, diverge {row[RunOracleStatus.Diverge]}, unsupported {row[RunOracleStatus.Unsupported]}, error {row[RunOracleStatus.Error]}");
        }
        int printed = 0;
        foreach (var c in all)
        {
            if (c.Status == RunOracleStatus.Agree || (c.Status == RunOracleStatus.Unsupported && !verbose))
            {
                continue;
            }
            if (printed++ >= MaxPrinted && !verbose)
            {
                Console.WriteLine("  ... (see --report for the full list)");
                break;
            }
            Console.WriteLine($"  {c.Status.ToString().ToUpperInvariant()} {c.Oracle} {Path.GetFileName(c.Source)}: {c.Detail}");
        }
        if (reportPath != null)
        {
            string full = Path.GetFullPath(reportPath);
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(full, BuildReport(results, input).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine($"report: {full}");
        }
        bool failed = all.Any(c => c.Status is RunOracleStatus.Diverge or RunOracleStatus.Error);
        return strict && failed ? 1 : 0;
    }
}
