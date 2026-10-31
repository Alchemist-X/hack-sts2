using System.Globalization;
using System.Text.Encodings.Web;

namespace Sts2Sim.Diff;

public sealed class DiffStats
{
    public int Bundles { get; private set; }

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

    public int SoftDiffs { get; private set; }

    public double AgreementRate => DecisionsCompared == 0 ? 0.0 : (double)DecisionsAgreed / DecisionsCompared;

    public void Add(BundleResult r)
    {
        Bundles++;
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
        SoftDiffs += r.SoftDiffs.Count;
        if (r.OutcomeChecked)
        {
            OutcomeChecked++;
            if (r.OutcomeAgreed)
            {
                OutcomeAgreed++;
            }
        }
    }

    public JsonObject ToJson() => new()
    {
        ["bundles"] = Bundles,
        ["agree"] = Agree,
        ["hard_divergence"] = HardDivergence,
        ["skipped"] = Skipped,
        ["error"] = Errors,
        ["sim_exception"] = SimExceptions,
        ["decisions_total"] = DecisionsTotal,
        ["decisions_compared"] = DecisionsCompared,
        ["decisions_agreed"] = DecisionsAgreed,
        ["decisions_unobserved"] = DecisionsUnobserved,
        ["decision_agreement_rate"] = Math.Round(AgreementRate, 6),
        ["outcome_checked"] = OutcomeChecked,
        ["outcome_agreed"] = OutcomeAgreed,
        ["soft_diffs"] = SoftDiffs
    };

    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"bundles {Bundles} (agree {Agree}, hard divergence {HardDivergence}, skipped {Skipped}, error {Errors}, sim exception {SimExceptions}); " +
        $"decisions agreed {DecisionsAgreed}/{DecisionsCompared} compared ({AgreementRate:P2}), {DecisionsUnobserved} unobserved, {DecisionsTotal} total; " +
        $"outcome agreed {OutcomeAgreed}/{OutcomeChecked}; soft diffs {SoftDiffs}");
}

public sealed record IgnoredFile(string Path, string Reason);

public static class DiffReport
{
    public const int SoftExamplesPerPattern = 3;

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string StatusName(BundleStatus s) => s switch
    {
        BundleStatus.Agree => "agree",
        BundleStatus.HardDivergence => "hard_divergence",
        BundleStatus.Skipped => "skipped",
        BundleStatus.Error => "error",
        _ => "sim_exception"
    };

    public static DiffStats Overall(IEnumerable<BundleResult> results)
    {
        var stats = new DiffStats();
        foreach (var r in results)
        {
            stats.Add(r);
        }
        return stats;
    }

    public static SortedDictionary<string, DiffStats> ByVersion(IEnumerable<BundleResult> results)
    {
        var map = new SortedDictionary<string, DiffStats>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            if (!map.TryGetValue(r.GameVersion, out var stats))
            {
                stats = new DiffStats();
                map[r.GameVersion] = stats;
            }
            stats.Add(r);
        }
        return map;
    }

    public static JsonObject Build(IReadOnlyList<BundleResult> results, IReadOnlyList<IgnoredFile> ignoredFiles, IReadOnlyList<string> inputs,
        bool includeIneligible = false)
    {
        var ordered = results.OrderBy(r => r.Path ?? string.Empty, StringComparer.Ordinal).ToList();
        var byVersion = new JsonObject();
        foreach (var (version, stats) in ByVersion(ordered))
        {
            byVersion[version] = stats.ToJson();
        }
        var divergences = new JsonArray();
        var exceptions = new JsonArray();
        var skipped = new JsonArray();
        var errors = new JsonArray();
        var bundles = new JsonArray();
        var ignoredKeys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in ordered)
        {
            bundles.Add(BundleSummary(r));
            switch (r.Status)
            {
                case BundleStatus.HardDivergence:
                    divergences.Add(DivergenceJson(r));
                    break;
                case BundleStatus.SimException:
                    exceptions.Add(ExceptionJson(r));
                    break;
                case BundleStatus.Skipped:
                    skipped.Add(new JsonObject { ["bundle"] = r.Path, ["game_version"] = r.GameVersion, ["reason"] = r.Reason });
                    break;
                case BundleStatus.Error:
                    errors.Add(new JsonObject { ["bundle"] = r.Path, ["game_version"] = r.GameVersion, ["reason"] = r.Reason });
                    break;
            }
            if (r.Bundle != null)
            {
                foreach (var key in r.Bundle.IgnoredKeys)
                {
                    ignoredKeys[key] = ignoredKeys.GetValueOrDefault(key) + 1;
                }
            }
        }
        var ignored = new JsonArray();
        foreach (var f in ignoredFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            ignored.Add(new JsonObject { ["path"] = f.Path, ["reason"] = f.Reason });
        }
        var unknownKeys = new JsonArray();
        foreach (var (key, count) in ignoredKeys)
        {
            unknownKeys.Add(new JsonObject { ["path"] = key, ["bundles"] = count });
        }
        var inputArray = new JsonArray();
        foreach (var input in inputs)
        {
            inputArray.Add(input);
        }
        return new JsonObject
        {
            ["report_version"] = 1,
            ["contract"] = "sim/specs/diff-bundle.md v1",
            ["inputs"] = inputArray,
            ["options"] = new JsonObject { ["include_ineligible"] = includeIneligible },
            ["summary"] = Overall(ordered).ToJson(),
            ["by_version"] = byVersion,
            ["divergences"] = divergences,
            ["sim_exceptions"] = exceptions,
            ["soft_summary"] = SoftSummary(ordered),
            ["skipped"] = skipped,
            ["errors"] = errors,
            ["ignored_files"] = ignored,
            ["unrecognized_bundle_keys"] = unknownKeys,
            ["bundles"] = bundles
        };
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(WriteOptions);

    public static JsonObject BundleSummary(BundleResult r)
    {
        var obj = new JsonObject
        {
            ["bundle"] = r.Path,
            ["game_version"] = r.GameVersion,
            ["status"] = StatusName(r.Status),
            ["decisions_total"] = r.DecisionsTotal,
            ["decisions_compared"] = r.DecisionsCompared,
            ["decisions_agreed"] = r.DecisionsAgreed,
            ["decisions_unobserved"] = r.DecisionsUnobserved,
            ["soft_diffs"] = r.SoftDiffs.Count
        };
        if (r.Reason != null)
        {
            obj["reason"] = r.Reason;
        }
        if (r.Divergence is { } d)
        {
            obj["first_divergence"] = $"{d.Kind} @ decision {d.DecisionIndex}: {d.First.Path}";
        }
        if (r.OutcomeChecked)
        {
            obj["sim_outcome"] = r.SimOutcome;
            obj["sim_final_hp"] = r.SimFinalHp;
        }
        return obj;
    }

    public static JsonObject DivergenceJson(BundleResult r)
    {
        var d = r.Divergence ?? throw new InvalidOperationException("The bundle result has no divergence.");
        var all = new JsonArray();
        foreach (var diff in d.AllHard)
        {
            all.Add(diff.ToJson());
        }
        var obj = new JsonObject
        {
            ["bundle"] = r.Path,
            ["game_version"] = r.GameVersion,
            ["source"] = SourceJson(r.Bundle),
            ["kind"] = d.Kind,
            ["decision_index"] = d.DecisionIndex,
            ["field_path"] = d.First.Path,
            ["oracle"] = d.First.Oracle?.DeepClone(),
            ["sim"] = d.First.Sim?.DeepClone()
        };
        if (d.First.Note != null)
        {
            obj["note"] = d.First.Note;
        }
        obj["all_hard"] = all;
        obj["action"] = d.Action?.ToJson();
        if (r.Bundle != null)
        {
            obj["setup"] = SetupJson(r.Bundle);
            obj["actions_prefix"] = ActionsPrefix(r.Bundle, d.DecisionIndex);
            if (d.DecisionIndex >= 0 && d.DecisionIndex < r.Bundle.Decisions.Count
                && r.Bundle.Decisions[d.DecisionIndex].OracleJson is { } provenance)
            {
                // The exporter's per-decision provenance (recorder action_seq / state_seq, settled, rolled_forward, ...):
                // where to look in the recording.
                obj["oracle_provenance"] = provenance.DeepClone();
            }
        }
        if (d.Kind == DivergenceKind.Final && r.Bundle != null)
        {
            obj["oracle_outcome"] = r.Bundle.Outcome;
            obj["oracle_final_hp"] = r.Bundle.FinalHp;
        }
        obj["oracle_projection"] = d.OracleProjection?.DeepClone();
        obj["sim_projection"] = d.SimProjection?.DeepClone();
        return obj;
    }

    public static JsonObject ExceptionJson(BundleResult r)
    {
        var obj = new JsonObject
        {
            ["bundle"] = r.Path,
            ["game_version"] = r.GameVersion,
            ["source"] = SourceJson(r.Bundle),
            ["decision_index"] = r.ExceptionDecisionIndex,
            ["message"] = r.Reason,
            ["detail"] = r.ExceptionDetail
        };
        if (r.Bundle != null)
        {
            obj["setup"] = SetupJson(r.Bundle);
            int prefix = r.ExceptionDecisionIndex is int i && i >= 0 ? i : 0;
            obj["actions_prefix"] = ActionsPrefix(r.Bundle, prefix);
            if (r.ExceptionDecisionIndex is int k && k >= 0 && k < r.Bundle.Decisions.Count)
            {
                obj["action"] = r.Bundle.Decisions[k].Action.ToJson();
            }
        }
        return obj;
    }

    public static JsonObject SetupJson(DiffBundle b)
    {
        var deck = new JsonArray();
        foreach (var c in b.Deck)
        {
            deck.Add(new JsonObject { ["id"] = c.Id, ["upgraded"] = c.Upgraded });
        }
        var offsets = new JsonObject();
        foreach (var (name, k) in b.RngOffsets)
        {
            offsets[name] = k;
        }
        var relics = new JsonArray();
        foreach (var relic in b.Relics)
        {
            relics.Add(relic);
        }
        return new JsonObject
        {
            ["seeding"] = b.Seeding,
            ["seed"] = b.Seed,
            ["rng_offsets"] = offsets,
            ["character"] = b.Character,
            ["ascension"] = b.Ascension,
            ["encounter_id"] = b.EncounterId,
            ["player"] = new JsonObject { ["hp"] = b.PlayerHp, ["max_hp"] = b.PlayerMaxHp },
            ["deck"] = deck,
            ["relics"] = relics
        };
    }

    public static JsonArray ActionsPrefix(DiffBundle b, int count)
    {
        var a = new JsonArray();
        int n = Math.Min(count, b.Decisions.Count);
        for (int i = 0; i < n; i++)
        {
            a.Add(b.Decisions[i].Action.ToJson());
        }
        return a;
    }

    public static JsonArray SoftSummary(IReadOnlyList<BundleResult> results)
    {
        var groups = new SortedDictionary<string, (int Count, HashSet<string> Bundles, List<JsonObject> Examples)>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            foreach (var soft in r.SoftDiffs)
            {
                string pattern = soft.Diff.PathPattern;
                if (!groups.TryGetValue(pattern, out var g))
                {
                    g = (0, new HashSet<string>(StringComparer.Ordinal), new List<JsonObject>());
                }
                g.Count++;
                g.Bundles.Add(r.Path ?? string.Empty);
                if (g.Examples.Count < SoftExamplesPerPattern)
                {
                    var ex = soft.Diff.ToJson();
                    ex["bundle"] = r.Path;
                    ex["decision_index"] = soft.DecisionIndex;
                    g.Examples.Add(ex);
                }
                groups[pattern] = g;
            }
        }
        var arr = new JsonArray();
        foreach (var (pattern, g) in groups)
        {
            var examples = new JsonArray();
            foreach (var ex in g.Examples)
            {
                examples.Add(ex);
            }
            arr.Add(new JsonObject
            {
                ["path_pattern"] = pattern,
                ["count"] = g.Count,
                ["bundles"] = g.Bundles.Count,
                ["examples"] = examples
            });
        }
        return arr;
    }

    private static JsonNode? SourceJson(DiffBundle? b)
    {
        if (b == null)
        {
            return null;
        }
        return b.SourceJson?.DeepClone() ?? BundleJson.WriteSource(b.Source);
    }
}
