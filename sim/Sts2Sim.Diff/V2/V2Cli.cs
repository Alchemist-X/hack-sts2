using System.Globalization;

namespace Sts2Sim.Diff.V2;

public static class V2Cli
{
    public const string Usage = "usage: Sts2Sim.Diff <bundle-dir-or-file> --v2 [--report out.json] [--baseline diff-baseline.json] [--update-baseline] [--include-ineligible] [-v|--verbose]";

    private const int MaxPrinted = 20;

    public static bool Wants(string[] args) => args.Any(a => a is "--v2" or "--baseline" or "--update-baseline");

    public static int RunMain(string[] args)
    {
        string? input = null;
        string? reportPath = null;
        string? baselinePath = null;
        bool update = false;
        bool verbose = false;
        bool includeIneligible = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--v2":
                    break;
                case "--report" or "--baseline":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine(Usage);
                        return 2;
                    }
                    if (arg == "--report") reportPath = args[++i];
                    else baselinePath = args[++i];
                    break;
                case "--update-baseline":
                    update = true;
                    break;
                case "--include-ineligible":
                    includeIneligible = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    if (input == null && !arg.StartsWith('-'))
                    {
                        input = arg;
                        break;
                    }
                    Console.Error.WriteLine($"unexpected argument '{arg}'");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        if (input == null || (update && baselinePath == null))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        var options = new V2Options { IncludeIneligible = includeIneligible };
        string? reportFull = reportPath != null ? Path.GetFullPath(reportPath) : null;
        string? baselineFull = baselinePath != null ? Path.GetFullPath(baselinePath) : null;
        List<string> files;
        try
        {
            files = Program.DiscoverFiles(input, reportFull).Where(f => !string.Equals(f, baselineFull, StringComparison.Ordinal)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"cannot read {input}: {ex.Message}");
            return 2;
        }
        var ignored = new List<IgnoredFile>();
        var unrecognized = new SortedSet<string>(StringComparer.Ordinal);
        var results = new List<SegmentResult>();
        foreach (var file in files)
        {
            results.AddRange(RunFile(file, ignored, unrecognized, options));
        }
        if (results.Count == 0)
        {
            Console.Error.WriteLine($"no diff bundles found under {input} ({ignored.Count} other JSON files ignored)");
            return 2;
        }

        RatchetResult? ratchet = null;
        bool writeBaseline = false;
        if (baselineFull != null)
        {
            if (File.Exists(baselineFull))
            {
                try
                {
                    var baseline = JsonNode.Parse(File.ReadAllText(baselineFull)) as JsonObject ?? throw new BundleFormatException("$", "baseline is not an object");
                    ratchet = Baseline.Check(baseline, results);
                }
                catch (Exception ex) when (ex is JsonException or BundleFormatException or IOException or InvalidOperationException or FormatException)
                {
                    Console.Error.WriteLine($"cannot read baseline {baselineFull}: {ex.Message}");
                    return 2;
                }
                writeBaseline = update && ratchet.Passed;
            }
            else
            {
                writeBaseline = true;
            }
        }

        Print(results, ignored, ratchet, verbose);
        if (reportFull != null)
        {
            var report = ReportV2.Build(results, ignored, new[] { Path.GetFullPath(input) }, options, ratchet, unrecognized);
            WriteText(reportFull, ReportV2.Serialize(report));
            Console.WriteLine($"report: {reportFull}");
        }
        if (writeBaseline)
        {
            WriteText(baselineFull!, ReportV2.Serialize(Baseline.Build(results)));
            Console.WriteLine($"baseline written: {baselineFull}");
        }
        else if (update && ratchet is { Passed: false })
        {
            Console.WriteLine("baseline not updated: the ratchet check failed");
        }
        bool failed = results.Any(r => r.Status is BundleStatus.HardDivergence or BundleStatus.Error or BundleStatus.SimException) || ratchet is { Passed: false };
        return failed ? 1 : 0;
    }

    private static void WriteText(string path, string text)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, text + Environment.NewLine);
    }

    public static List<SegmentResult> RunFile(string file, List<IgnoredFile> ignored, ISet<string> unrecognized, V2Options options)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<SegmentResult> { FileError(file, BundleRunner.UnknownVersion, "cannot read file: " + ex.Message) };
        }
        string version = BundleRunner.UnknownVersion;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!BundleJson.LooksLikeBundle(doc.RootElement))
            {
                ignored.Add(new IgnoredFile(file, "no top-level bundle_version (not a diff bundle)"));
                return new List<SegmentResult>();
            }
            if (doc.RootElement.TryGetProperty("game_version", out var v) && v.ValueKind == JsonValueKind.String)
            {
                version = v.GetString()!;
            }
            var run = RunBundleJson.ReadAny(doc.RootElement, file);
            foreach (var key in run.IgnoredKeys)
            {
                unrecognized.Add(key);
            }
            return V2Runner.RunBundle(run, options);
        }
        catch (JsonException ex)
        {
            return new List<SegmentResult> { FileError(file, version, "invalid JSON: " + ex.Message) };
        }
        catch (BundleFormatException ex)
        {
            return new List<SegmentResult> { FileError(file, version, "bundle format: " + ex.Message) };
        }
        catch (Exception ex)
        {
            var r = FileError(file, version, $"harness error {ex.GetType().Name}: {ex.Message}");
            r.ExceptionDetail = ex.ToString();
            r.Class = DivergenceClass.HarnessBug;
            return new List<SegmentResult> { r };
        }
    }

    private static SegmentResult FileError(string file, string version, string reason) => new()
    {
        Path = file,
        Lineage = Path.GetFileNameWithoutExtension(file),
        SegmentId = "<bundle>",
        SegmentKind = "bundle",
        GameVersion = version,
        Status = BundleStatus.Error,
        Class = DivergenceClass.ExporterBug,
        Reason = reason
    };

    private static void Print(List<SegmentResult> results, List<IgnoredFile> ignored, RatchetResult? ratchet, bool verbose)
    {
        Console.WriteLine("overall: " + ReportV2.Stats(results).Describe());
        foreach (var vg in results.GroupBy(r => r.GameVersion).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {vg.Key}: {ReportV2.Stats(vg).Describe()}");
            foreach (var kg in vg.GroupBy(r => r.SegmentKind).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var classes = kg.Where(r => r.Status != BundleStatus.Agree).GroupBy(r => r.Class).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => $"{g.Key} {g.Count()}");
                Console.WriteLine($"    {kg.Key}: {kg.Count()} segments, agree {kg.Count(r => r.Status == BundleStatus.Agree)}; not agreeing by class: {string.Join(", ", classes.DefaultIfEmpty("none"))}");
            }
        }
        if (ignored.Count > 0)
        {
            Console.WriteLine($"ignored non-bundle files: {ignored.Count}");
        }
        if (ratchet != null)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ratchet: {(ratchet.Passed ? "pass" : "FAIL")} ({ratchet.SegmentsChecked} baseline segments, {ratchet.DecisionsChecked} agreed decisions checked, {ratchet.Violations.Count} violations, {ratchet.NewlyAgreeing.Count} newly agreeing)"));
            foreach (var v in ratchet.Violations.Take(verbose ? int.MaxValue : MaxPrinted))
            {
                Console.WriteLine($"  RATCHET {v.Segment}{(v.DecisionIndex is int i ? " @ " + i.ToString(CultureInfo.InvariantCulture) : string.Empty)}: {v.Reason}");
            }
        }
        int printed = 0;
        foreach (var r in results)
        {
            if (r.Status == BundleStatus.Agree || (r.Status == BundleStatus.Skipped && !verbose))
            {
                continue;
            }
            if (printed++ >= MaxPrinted && !verbose)
            {
                Console.WriteLine("  ... (see --report for the full list)");
                break;
            }
            string tag = r.Status switch
            {
                BundleStatus.HardDivergence => "DIVERGE",
                BundleStatus.SimException => "EXCEPTION",
                BundleStatus.Error => "ERROR",
                _ => "SKIP"
            };
            Console.WriteLine($"  {tag} {r.Key} [{r.GameVersion} {r.SegmentKind}] {r.Class}: {r.Reason}");
        }
    }
}
