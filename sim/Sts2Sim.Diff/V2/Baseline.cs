namespace Sts2Sim.Diff.V2;

public sealed record RatchetViolation(string Segment, int? DecisionIndex, string Reason)
{
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["segment"] = Segment, ["reason"] = Reason };
        if (DecisionIndex is int i)
        {
            o["decision"] = i;
        }
        return o;
    }
}

public sealed class RatchetResult
{
    public int SegmentsChecked { get; set; }

    public int DecisionsChecked { get; set; }

    public List<RatchetViolation> Violations { get; } = new();

    public List<string> ChangedBundles { get; } = new();

    public List<string> NewlyAgreeing { get; } = new();

    public bool Passed => Violations.Count == 0;

    public JsonObject ToJson()
    {
        var violations = new JsonArray();
        foreach (var v in Violations)
        {
            violations.Add(v.ToJson());
        }
        return new JsonObject
        {
            ["passed"] = Passed,
            ["segments_checked"] = SegmentsChecked,
            ["decisions_checked"] = DecisionsChecked,
            ["violations"] = violations,
            ["changed_segments"] = new JsonArray(ChangedBundles.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["newly_agreeing_segments"] = new JsonArray(NewlyAgreeing.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
        };
    }
}

public static class Baseline
{
    public const int Version = 1;

    public static JsonObject Build(IReadOnlyList<SegmentResult> results)
    {
        var segments = new JsonObject();
        foreach (var r in results.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (segments.ContainsKey(r.Key))
            {
                continue;
            }
            var entry = new JsonObject
            {
                ["sha256"] = r.Sha256,
                ["status"] = DiffReport.StatusName(r.Status),
                ["class"] = r.Class,
                ["compared"] = r.DecisionsCompared,
                ["agreed"] = new JsonArray(r.AgreedIndices.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
            };
            if (r.Divergence is { } d)
            {
                entry["first_divergence"] = new JsonObject { ["decision"] = d.DecisionIndex, ["kind"] = d.Kind, ["path"] = d.First.Path };
            }
            segments[r.Key] = entry;
        }
        var coverage = CoverageCounter.From(results).ToJson(perId: false);
        return new JsonObject
        {
            ["baseline_version"] = Version,
            ["harness"] = "Sts2Sim.Diff v2",
            ["segments"] = segments,
            ["coverage"] = coverage
        };
    }

    public static RatchetResult Check(JsonObject baseline, IReadOnlyList<SegmentResult> results)
    {
        if (baseline["baseline_version"] is not JsonValue v || !v.TryGetValue(out int version) || version != Version)
        {
            throw new BundleFormatException("$.baseline_version", $"unsupported baseline (expected version {Version})");
        }
        var byKey = new Dictionary<string, SegmentResult>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            byKey.TryAdd(r.Key, r);
        }
        var ratchet = new RatchetResult();
        var segments = baseline["segments"] as JsonObject ?? new JsonObject();
        foreach (var (key, node) in segments)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }
            var agreed = (entry["agreed"] as JsonArray ?? new JsonArray()).Select(x => x!.GetValue<int>()).ToList();
            string? status = entry["status"]?.GetValue<string>();
            ratchet.SegmentsChecked++;
            if (!byKey.TryGetValue(key, out var current))
            {
                if (agreed.Count > 0)
                {
                    ratchet.Violations.Add(new RatchetViolation(key, null, $"segment missing from this run ({agreed.Count} agreed decisions in the baseline)"));
                }
                continue;
            }
            if (entry["sha256"]?.GetValue<string>() is { } sha && sha != current.Sha256)
            {
                ratchet.ChangedBundles.Add(key);
            }
            var now = new HashSet<int>(current.AgreedIndices);
            foreach (int i in agreed)
            {
                ratchet.DecisionsChecked++;
                if (!now.Contains(i))
                {
                    string why = current.Divergence is { } d
                        ? $"agreed in the baseline, now {current.Class} {d.Kind} at decision {d.DecisionIndex}: {d.First.Path}"
                        : $"agreed in the baseline, now {DiffReport.StatusName(current.Status)}: {current.Reason}";
                    ratchet.Violations.Add(new RatchetViolation(key, i, why));
                }
            }
            if (status == "agree" && current.Status != BundleStatus.Agree && agreed.All(now.Contains))
            {
                ratchet.Violations.Add(new RatchetViolation(key, null, $"agreed in the baseline, now {DiffReport.StatusName(current.Status)} ({current.Reason})"));
            }
            if (status != "agree" && current.Status == BundleStatus.Agree)
            {
                ratchet.NewlyAgreeing.Add(key);
            }
        }
        foreach (var r in results)
        {
            if (!segments.ContainsKey(r.Key) && r.Status == BundleStatus.Agree)
            {
                ratchet.NewlyAgreeing.Add(r.Key);
            }
        }
        return ratchet;
    }
}
