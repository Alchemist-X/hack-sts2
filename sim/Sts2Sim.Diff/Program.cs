using System.Globalization;

namespace Sts2Sim.Diff;

public static class Program
{
    private const int MaxPrintedDivergences = 20;

    private const string Usage = "usage: Sts2Sim.Diff <bundle-dir-or-file> [--report out.json] [--include-ineligible] [-v|--verbose]";

    public static int Main(string[] args)
    {
        string? input = null;
        string? reportPath = null;
        bool verbose = false;
        bool includeIneligible = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--report")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine(Usage);
                    return 2;
                }
                reportPath = args[++i];
            }
            else if (arg is "-v" or "--verbose")
            {
                verbose = true;
            }
            else if (arg == "--include-ineligible")
            {
                includeIneligible = true;
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

        string? reportFull = reportPath != null ? Path.GetFullPath(reportPath) : null;
        List<string> files;
        try
        {
            files = DiscoverFiles(input, reportFull);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"cannot read {input}: {ex.Message}");
            return 2;
        }

        var results = new List<BundleResult>(files.Count);
        var ignored = new List<IgnoredFile>();
        foreach (var file in files)
        {
            var result = RunFile(file, ignored, includeIneligible);
            if (result != null)
            {
                results.Add(result);
            }
        }
        if (results.Count == 0)
        {
            Console.Error.WriteLine($"no diff bundles found under {input} ({ignored.Count} other JSON files ignored)");
            if (reportFull != null)
            {
                WriteReport(reportFull, results, ignored, input, includeIneligible);
            }
            return 2;
        }

        PrintSummary(results, ignored, verbose);
        if (reportFull != null)
        {
            WriteReport(reportFull, results, ignored, input, includeIneligible);
            Console.WriteLine($"report: {reportFull}");
        }
        bool failed = results.Any(r => r.Status is BundleStatus.HardDivergence or BundleStatus.Error or BundleStatus.SimException);
        return failed ? 1 : 0;
    }

    public static List<string> DiscoverFiles(string input, string? excludeFullPath)
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
        return Directory.EnumerateFiles(full, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(f => excludeFullPath == null || !string.Equals(f, excludeFullPath, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static BundleResult? RunFile(string file, List<IgnoredFile> ignored, bool includeIneligible = false)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BundleResult { Path = file, Status = BundleStatus.Error, Reason = "cannot read file: " + ex.Message };
        }
        string version = BundleRunner.UnknownVersion;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!BundleJson.LooksLikeBundle(doc.RootElement))
            {
                ignored.Add(new IgnoredFile(file, "no top-level bundle_version (not a diff bundle)"));
                return null;
            }
            if (doc.RootElement.TryGetProperty("game_version", out var v) && v.ValueKind == JsonValueKind.String)
            {
                version = v.GetString()!;
            }
            var bundle = BundleJson.Read(doc.RootElement, file);
            return BundleRunner.Run(bundle, includeIneligible);
        }
        catch (JsonException ex)
        {
            return new BundleResult { Path = file, Status = BundleStatus.Error, Reason = "invalid JSON: " + ex.Message };
        }
        catch (BundleFormatException ex)
        {
            return new BundleResult { Path = file, GameVersion = version, Status = BundleStatus.Error, Reason = "bundle format: " + ex.Message };
        }
        catch (Exception ex)
        {
            return new BundleResult
            {
                Path = file,
                GameVersion = version,
                Status = BundleStatus.Error,
                Reason = $"harness error {ex.GetType().Name}: {ex.Message}",
                ExceptionDetail = ex.ToString()
            };
        }
    }

    private static void WriteReport(string path, List<BundleResult> results, List<IgnoredFile> ignored, string input, bool includeIneligible)
    {
        var report = DiffReport.Build(results, ignored, new[] { Path.GetFullPath(input) }, includeIneligible);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, DiffReport.Serialize(report) + Environment.NewLine);
    }

    private static void PrintSummary(List<BundleResult> results, List<IgnoredFile> ignored, bool verbose)
    {
        Console.WriteLine("overall: " + DiffReport.Overall(results).Describe());
        foreach (var (version, stats) in DiffReport.ByVersion(results))
        {
            Console.WriteLine($"  {version}: {stats.Describe()}");
        }
        if (ignored.Count > 0)
        {
            Console.WriteLine($"ignored non-bundle files: {ignored.Count}");
        }
        int printed = 0;
        foreach (var r in results.OrderBy(r => r.Path ?? string.Empty, StringComparer.Ordinal))
        {
            if (r.Status == BundleStatus.Agree || (r.Status == BundleStatus.Skipped && !verbose))
            {
                continue;
            }
            if (printed++ >= MaxPrintedDivergences && !verbose)
            {
                Console.WriteLine("  ... (see --report for the full list)");
                break;
            }
            string name = r.Path != null ? Path.GetFileName(r.Path) : "<memory>";
            switch (r.Status)
            {
                case BundleStatus.HardDivergence when r.Divergence is { } d:
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  DIVERGE {name} [{r.GameVersion}] {d.Kind} @ decision {d.DecisionIndex}: {d.First.Path} oracle={Show(d.First.Oracle)} sim={Show(d.First.Sim)}{(d.First.Note != null ? " (" + d.First.Note + ")" : string.Empty)}"));
                    break;
                case BundleStatus.SimException:
                    Console.WriteLine($"  EXCEPTION {name} [{r.GameVersion}] @ decision {r.ExceptionDecisionIndex}: {r.Reason}");
                    break;
                case BundleStatus.Error:
                    Console.WriteLine($"  ERROR {name} [{r.GameVersion}]: {r.Reason}");
                    break;
                case BundleStatus.Skipped:
                    Console.WriteLine($"  SKIP {name} [{r.GameVersion}]: {r.Reason}");
                    break;
            }
            if (verbose)
            {
                foreach (var soft in r.SoftDiffs)
                {
                    Console.WriteLine($"    soft @ {soft.DecisionIndex}: {soft.Diff.Path} oracle={Show(soft.Diff.Oracle)} sim={Show(soft.Diff.Sim)}");
                }
            }
        }
    }

    private static string Show(JsonNode? node) => node == null ? "<none>" : node.ToJsonString(DiffReport.WriteOptions).Replace(Environment.NewLine, " ").Replace("  ", string.Empty);
}
