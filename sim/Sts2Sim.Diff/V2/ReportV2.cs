using System.Globalization;

namespace Sts2Sim.Diff.V2;

public sealed class V2Stats
{
    public int Segments { get; private set; }

    public int Agree { get; private set; }

    public int HardDivergence { get; private set; }

    public int Skipped { get; private set; }

    public int Errors { get; private set; }

    public int SimExceptions { get; private set; }

    public long DecisionsTotal { get; private set; }

    public long DecisionsCompared { get; private set; }

    public long DecisionsAgreed { get; private set; }

    public long DecisionsUnobserved { get; private set; }

    public int OutcomeChecked { get; private set; }

    public int OutcomeAgreed { get; private set; }

    public int FloorSummaryChecked { get; private set; }

    public int FloorSummaryAgreed { get; private set; }

    public int SoftDiffs { get; private set; }

    public double AgreementRate => DecisionsCompared == 0 ? 0.0 : (double)DecisionsAgreed / DecisionsCompared;

    public void Add(SegmentResult r)
    {
        Segments++;
        switch (r.Status)
        {
            case BundleStatus.Agree: Agree++; break;
            case BundleStatus.HardDivergence: HardDivergence++; break;
            case BundleStatus.Skipped: Skipped++; break;
            case BundleStatus.Error: Errors++; break;
            case BundleStatus.SimException: SimExceptions++; break;
        }
        if (r.Status is BundleStatus.Skipped or BundleStatus.Error)
        {
            return;
        }
        DecisionsTotal += r.DecisionsTotal;
        DecisionsCompared += r.DecisionsCompared;
        DecisionsAgreed += r.DecisionsAgreed;
        DecisionsUnobserved += r.DecisionsUnobserved;
        if (r.OutcomeChecked)
        {
            OutcomeChecked++;
            if (r.OutcomeAgreed) OutcomeAgreed++;
        }
        if (r.FloorSummaryChecked)
        {
            FloorSummaryChecked++;
            if (r.FloorSummaryAgreed) FloorSummaryAgreed++;
        }
        SoftDiffs += r.SoftDiffs.Count;
    }

    public JsonObject ToJson() => new()
    {
        ["segments"] = Segments,
        ["agree"] = Agree,
        ["hard_divergence"] = HardDivergence,
        ["skipped"] = Skipped,
        ["error"] = Errors,
        ["sim_exception"] = SimExceptions,
        ["decisions_total"] = DecisionsTotal,
        ["decisions_compared"] = DecisionsCompared,
        ["decisions_agreed"] = DecisionsAgreed,
        ["decisions_unobserved"] = DecisionsUnobserved,
        ["decision_agreement_rate"] = AgreementRate,
        ["outcome_checked"] = OutcomeChecked,
        ["outcome_agreed"] = OutcomeAgreed,
        ["floor_summary_checked"] = FloorSummaryChecked,
        ["floor_summary_agreed"] = FloorSummaryAgreed,
        ["soft_diffs"] = SoftDiffs
    };

    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"segments {Segments} (agree {Agree}, hard divergence {HardDivergence}, skipped {Skipped}, error {Errors}, sim exception {SimExceptions}); decisions agreed {DecisionsAgreed}/{DecisionsCompared} compared ({AgreementRate * 100:F2} %), {DecisionsUnobserved} unobserved, {DecisionsTotal} total; outcome agreed {OutcomeAgreed}/{OutcomeChecked}; floor summary agreed {FloorSummaryAgreed}/{FloorSummaryChecked}; soft diffs {SoftDiffs}");
}

public static class ReportV2
{
    public const int Version = 2;

    public static V2Stats Stats(IEnumerable<SegmentResult> results)
    {
        var s = new V2Stats();
        foreach (var r in results)
        {
            s.Add(r);
        }
        return s;
    }

    public static JsonObject Build(IReadOnlyList<SegmentResult> results, IReadOnlyList<IgnoredFile> ignored, IReadOnlyList<string> inputs,
        V2Options options, RatchetResult? ratchet, IReadOnlyCollection<string> unrecognizedKeys)
    {
        var byVersion = new JsonObject();
        foreach (var vg in results.GroupBy(r => r.GameVersion).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var byKind = new JsonObject();
            foreach (var kg in vg.GroupBy(r => r.SegmentKind).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var byClass = new JsonObject();
                foreach (var cg in kg.Where(r => r.Status != BundleStatus.Agree).GroupBy(r => r.Class).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    byClass[cg.Key] = new JsonObject
                    {
                        ["count"] = cg.Count(),
                        ["statuses"] = StatusCounts(cg),
                        ["segments"] = new JsonArray(cg.Select(r => (JsonNode?)JsonValue.Create(r.Key)).Take(50).ToArray())
                    };
                }
                var kindObj = Stats(kg).ToJson();
                kindObj["by_first_divergence_class"] = byClass;
                byKind[kg.Key] = kindObj;
            }
            var versionObj = Stats(vg).ToJson();
            versionObj["by_segment_kind"] = byKind;
            byVersion[vg.Key] = versionObj;
        }

        var divergences = new JsonArray();
        var exceptions = new JsonArray();
        var skipped = new JsonArray();
        var errors = new JsonArray();
        foreach (var r in results.OrderBy(r => r.Path ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.SegmentId, StringComparer.Ordinal))
        {
            switch (r.Status)
            {
                case BundleStatus.HardDivergence:
                    divergences.Add(DivergenceJson(r));
                    break;
                case BundleStatus.SimException:
                    exceptions.Add(ExceptionJson(r));
                    break;
                case BundleStatus.Skipped:
                    skipped.Add(new JsonObject { ["segment"] = r.Key, ["class"] = r.Class, ["reason"] = r.Reason, ["bundle"] = r.Path });
                    break;
                case BundleStatus.Error:
                    errors.Add(new JsonObject { ["segment"] = r.Key, ["class"] = r.Class, ["reason"] = r.Reason, ["bundle"] = r.Path });
                    break;
            }
        }
        var soft = new JsonObject();
        foreach (var g in results.SelectMany(r => r.SoftDiffs.Select(s => (r, s))).GroupBy(x => x.s.Diff.PathPattern).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            soft[g.Key] = new JsonObject
            {
                ["count"] = g.Count(),
                ["segments"] = g.Select(x => x.r.Key).Distinct().Count(),
                ["examples"] = new JsonArray(g.Take(DiffReport.SoftExamplesPerPattern).Select(x => (JsonNode?)new JsonObject
                {
                    ["segment"] = x.r.Key,
                    ["decision_index"] = x.s.DecisionIndex,
                    ["diff"] = x.s.Diff.ToJson()
                }).ToArray())
            };
        }
        var segments = new JsonArray();
        foreach (var r in results)
        {
            segments.Add(new JsonObject
            {
                ["segment"] = r.Key,
                ["bundle"] = r.Path,
                ["game_version"] = r.GameVersion,
                ["kind"] = r.SegmentKind,
                ["start"] = r.StartMode,
                ["from_v1"] = r.FromV1,
                ["status"] = DiffReport.StatusName(r.Status),
                ["class"] = r.Class,
                ["decisions_total"] = r.DecisionsTotal,
                ["decisions_compared"] = r.DecisionsCompared,
                ["decisions_agreed"] = r.DecisionsAgreed,
                ["decisions_unobserved"] = r.DecisionsUnobserved,
                ["reason"] = r.Reason
            });
        }
        var report = new JsonObject
        {
            ["report_version"] = Version,
            ["contract"] = "docs/sim-port/design-diff-gate.md §5 (bundle v2; v1 read as one combat segment); sim/specs/diff-harness-v2.md",
            ["inputs"] = new JsonArray(inputs.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
            ["options"] = new JsonObject { ["include_ineligible"] = options.IncludeIneligible },
            ["classification"] = "heuristic: exception -> port_bug or harness_bug by throwing namespace (NotImplementedException -> content_not_ported); projection field mismatch -> port_bug candidate; field the sim does not project -> projection_gap; any unported id touched at the divergent decision -> content_not_ported",
            ["summary"] = Stats(results).ToJson(),
            ["by_version"] = byVersion,
            ["coverage"] = CoverageCounter.From(results).ToJson(),
            ["divergences"] = divergences,
            ["sim_exceptions"] = exceptions,
            ["soft_summary"] = soft,
            ["skipped"] = skipped,
            ["errors"] = errors,
            ["ignored_files"] = new JsonArray(ignored.Select(f => (JsonNode?)new JsonObject { ["path"] = f.Path, ["reason"] = f.Reason }).ToArray()),
            ["unrecognized_bundle_keys"] = new JsonArray(unrecognizedKeys.OrderBy(k => k, StringComparer.Ordinal).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["segments"] = segments
        };
        if (ratchet != null)
        {
            report["ratchet"] = ratchet.ToJson();
        }
        return report;
    }

    private static JsonObject StatusCounts(IEnumerable<SegmentResult> results)
    {
        var o = new JsonObject();
        foreach (var g in results.GroupBy(r => DiffReport.StatusName(r.Status)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            o[g.Key] = g.Count();
        }
        return o;
    }

    public static JsonObject DivergenceJson(SegmentResult r)
    {
        var d = r.Divergence!;
        var allHard = new JsonArray();
        foreach (var diff in d.AllHard)
        {
            allHard.Add(diff.ToJson());
        }
        return new JsonObject
        {
            ["segment"] = r.Key,
            ["bundle"] = r.Path,
            ["game_version"] = r.GameVersion,
            ["segment_kind"] = r.SegmentKind,
            ["start"] = r.StartMode,
            ["class"] = r.Class,
            ["kind"] = d.Kind,
            ["decision_index"] = d.DecisionIndex,
            ["field_path"] = d.First.Path,
            ["oracle"] = d.First.Oracle?.DeepClone(),
            ["sim"] = d.First.Sim?.DeepClone(),
            ["note"] = d.First.Note,
            ["all_hard"] = allHard,
            ["action"] = d.Action?.DeepClone(),
            ["actions_prefix"] = new JsonArray(r.ActionsPrefix.Select(a => a?.DeepClone()).ToArray()),
            ["oracle_projection"] = d.OracleProjection?.DeepClone(),
            ["sim_projection"] = d.SimProjection?.DeepClone()
        };
    }

    public static JsonObject ExceptionJson(SegmentResult r) => new()
    {
        ["segment"] = r.Key,
        ["bundle"] = r.Path,
        ["game_version"] = r.GameVersion,
        ["class"] = r.Class,
        ["decision_index"] = r.ExceptionDecisionIndex,
        ["reason"] = r.Reason,
        ["actions_prefix"] = new JsonArray(r.ActionsPrefix.Select(a => a?.DeepClone()).ToArray()),
        ["exception"] = r.ExceptionDetail
    };

    public static string Serialize(JsonNode node) => node.ToJsonString(DiffReport.WriteOptions);
}
