using System.Globalization;

namespace Sts2Sim.Diff.V2;

public static class RunScreenComparer
{
    public const string NotProjectedNote = "the sim projection has no such field";

    private static readonly HashSet<string> SoftLeafNames = new(StringComparer.Ordinal) { "title", "description", "label", "text", "site_id", "picked" };

    private static readonly HashSet<string> SoftPaths = new(StringComparer.Ordinal) { "select.kind", "select.min", "select.max" };

    private static readonly HashSet<string> MultisetPaths = new(StringComparer.Ordinal) { "deck" };

    private static readonly string[] FloorSummaryProjected = { "map_point_type", "rooms", "gold_gained" };

    public static bool IsCombatProjection(JsonNode? node) => node is JsonObject o && o.ContainsKey("turn") && o.ContainsKey("player");

    public static List<FieldDiff> Compare(JsonNode? oracle, JsonObject sim, ISet<string>? ignoredKeys = null)
    {
        var diffs = new List<FieldDiff>();
        if (oracle is not JsonObject o)
        {
            diffs.Add(new FieldDiff("pre_state", oracle?.DeepClone(), null, DiffSeverity.Hard, "a run-level pre_state must be an object"));
            return diffs;
        }
        foreach (var (key, value) in o)
        {
            if (key == "combat" && IsCombatProjection(value) && sim["combat"] is JsonObject simCombat)
            {
                CompareCombat(value!, simCombat, "combat", diffs, ignoredKeys);
                continue;
            }
            if (!sim.TryGetPropertyValue(key, out var simValue))
            {
                if (value != null)
                {
                    diffs.Add(new FieldDiff(key, value.DeepClone(), null, IsSoft(key) ? DiffSeverity.Soft : DiffSeverity.Hard, NotProjectedNote));
                }
                continue;
            }
            if (MultisetPaths.Contains(key))
            {
                CompareMultiset(key, value, simValue, diffs);
                continue;
            }
            CompareNode(key, value, simValue, diffs);
        }
        return OrderHardFirst(diffs);
    }

    public static void CompareCombat(JsonNode oracle, JsonNode sim, string prefix, List<FieldDiff> diffs, ISet<string>? ignoredKeys)
    {
        var o = ProjectionJson.Read(oracle, prefix, ignoredKeys);
        var s = ProjectionJson.Read(sim, prefix);
        foreach (var d in ProjectionComparer.Compare(o, s))
        {
            diffs.Add(d with { Path = prefix + "." + d.Path });
        }
    }

    public static List<FieldDiff> CompareFloorSummary(JsonObject oracle, JsonObject sim)
    {
        var diffs = new List<FieldDiff>();
        var normalized = (JsonObject)Normalize(oracle)!;
        foreach (var (key, value) in normalized)
        {
            string path = "floor_summary." + key;
            if (Array.IndexOf(FloorSummaryProjected, key) < 0)
            {
                diffs.Add(new FieldDiff(path, null, null, DiffSeverity.Soft, "not projected by the sim (compared on the oracle side only)"));
                continue;
            }
            if (key == "rooms" && value is JsonArray oracleRooms && sim["rooms"] is JsonArray simRooms)
            {
                if (oracleRooms.Count != simRooms.Count)
                {
                    diffs.Add(new FieldDiff(path + ".length", oracleRooms.Count, simRooms.Count, DiffSeverity.Hard));
                }
                for (int i = 0; i < Math.Min(oracleRooms.Count, simRooms.Count); i++)
                {
                    if (oracleRooms[i] is not JsonObject or || simRooms[i] is not JsonObject sr)
                    {
                        continue;
                    }
                    foreach (var (rk, rv) in or)
                    {
                        string rp = $"{path}[{i}].{rk}";
                        if (!sr.TryGetPropertyValue(rk, out var sv))
                        {
                            diffs.Add(new FieldDiff(rp, null, null, DiffSeverity.Soft, "not projected by the sim"));
                            continue;
                        }
                        CompareNode(rp, rv, sv, diffs);
                    }
                }
                continue;
            }
            CompareNode(path, value, sim[key], diffs);
        }
        return OrderHardFirst(diffs);
    }

    public static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (k, v) in o)
                {
                    copy[k] = Normalize(v);
                }
                return copy;
            case JsonArray a:
                var arr = new JsonArray();
                foreach (var v in a)
                {
                    arr.Add(Normalize(v));
                }
                return arr;
            case JsonValue v when v.TryGetValue(out string? s) && s != null:
                return JsonValue.Create(RunScreenProjector.StripCategory(s));
            default:
                return node?.DeepClone();
        }
    }

    private static List<FieldDiff> OrderHardFirst(List<FieldDiff> diffs)
        => diffs.Where(d => d.Severity == DiffSeverity.Hard).Concat(diffs.Where(d => d.Severity == DiffSeverity.Soft)).ToList();

    private static bool IsSoft(string path)
    {
        if (SoftPaths.Contains(FieldDiff.PatternOf(path)))
        {
            return true;
        }
        int dot = path.LastIndexOf('.');
        string leaf = dot >= 0 ? path[(dot + 1)..] : path;
        int bracket = leaf.IndexOf('[');
        if (bracket >= 0)
        {
            leaf = leaf[..bracket];
        }
        return SoftLeafNames.Contains(leaf);
    }

    public static void CompareNode(string path, JsonNode? oracle, JsonNode? sim, List<FieldDiff> diffs)
    {
        var severity = IsSoft(path) ? DiffSeverity.Soft : DiffSeverity.Hard;
        switch (oracle)
        {
            case null:
                if (sim != null)
                {
                    diffs.Add(new FieldDiff(path, null, sim.DeepClone(), severity));
                }
                return;
            case JsonObject oo:
                if (sim is not JsonObject so)
                {
                    diffs.Add(new FieldDiff(path, oo.DeepClone(), sim?.DeepClone(), severity, "type differs"));
                    return;
                }
                foreach (var (k, v) in oo)
                {
                    string p = path + "." + k;
                    if (!so.TryGetPropertyValue(k, out var sv))
                    {
                        if (v != null)
                        {
                            diffs.Add(new FieldDiff(p, v.DeepClone(), null, IsSoft(p) ? DiffSeverity.Soft : DiffSeverity.Hard, NotProjectedNote));
                        }
                        continue;
                    }
                    CompareNode(p, v, sv, diffs);
                }
                return;
            case JsonArray oa:
                if (sim is not JsonArray sa)
                {
                    diffs.Add(new FieldDiff(path, oa.DeepClone(), sim?.DeepClone(), severity, "type differs"));
                    return;
                }
                if (oa.Count != sa.Count)
                {
                    diffs.Add(new FieldDiff(path + ".length", oa.Count, sa.Count, severity));
                }
                for (int i = 0; i < Math.Min(oa.Count, sa.Count); i++)
                {
                    CompareNode($"{path}[{i}]", oa[i], sa[i], diffs);
                }
                return;
            default:
                if (!ScalarEquals(oracle, sim))
                {
                    diffs.Add(new FieldDiff(path, oracle.DeepClone(), sim?.DeepClone(), severity));
                }
                return;
        }
    }

    public static bool ScalarEquals(JsonNode? a, JsonNode? b)
    {
        if (a == null || b == null)
        {
            return a == null && b == null;
        }
        if (a is not JsonValue va || b is not JsonValue vb)
        {
            return JsonNode.DeepEquals(a, b);
        }
        var ka = va.GetValueKind();
        var kb = vb.GetValueKind();
        if (ka == JsonValueKind.Number && kb == JsonValueKind.Number)
        {
            return decimal.Parse(va.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
                == decimal.Parse(vb.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (ka == JsonValueKind.String && kb == JsonValueKind.String)
        {
            return string.Equals(RunScreenProjector.StripCategory(va.GetValue<string>()), RunScreenProjector.StripCategory(vb.GetValue<string>()), StringComparison.Ordinal);
        }
        return JsonNode.DeepEquals(a, b);
    }

    public static string CardMultisetKey(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            string id = o["id"] is JsonValue iv && iv.TryGetValue(out string? s) && s != null ? RunScreenProjector.StripCategory(s) : "?";
            bool up = o["upgraded"] is JsonValue uv && uv.TryGetValue(out bool b) && b;
            string key = ProjectionJson.CardKey(id, up);
            if (o["enchantment"] is JsonObject e && e["id"] is JsonValue ev && ev.TryGetValue(out string? eid) && eid != null)
            {
                key += "{" + RunScreenProjector.StripCategory(eid) + (e["amount"] is JsonValue av ? ":" + av.ToJsonString() : string.Empty) + "}";
            }
            return key;
        }
        return node is JsonValue v && v.TryGetValue(out string? str) && str != null ? RunScreenProjector.StripCategory(str) : node?.ToJsonString() ?? "null";
    }

    public static void CompareMultiset(string path, JsonNode? oracle, JsonNode? sim, List<FieldDiff> diffs)
    {
        if (oracle is not JsonArray oa || sim is not JsonArray sa)
        {
            CompareNode(path, oracle, sim, diffs);
            return;
        }
        var o = oa.Select(CardMultisetKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var s = sa.Select(CardMultisetKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (!o.SequenceEqual(s, StringComparer.Ordinal))
        {
            var missing = Except(o, s);
            var extra = Except(s, o);
            diffs.Add(new FieldDiff(path, new JsonArray(o.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), new JsonArray(s.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), DiffSeverity.Hard,
                $"multiset differs: oracle-only [{string.Join(", ", missing)}], sim-only [{string.Join(", ", extra)}]"));
        }
        else if (!oa.Select(CardMultisetKey).SequenceEqual(sa.Select(CardMultisetKey), StringComparer.Ordinal))
        {
            diffs.Add(new FieldDiff(path + ".order", null, null, DiffSeverity.Soft, "same multiset, different order"));
        }
    }

    private static List<string> Except(List<string> a, List<string> b)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in b)
        {
            counts[x] = counts.GetValueOrDefault(x) + 1;
        }
        var result = new List<string>();
        foreach (var x in a)
        {
            if (counts.TryGetValue(x, out int c) && c > 0)
            {
                counts[x] = c - 1;
            }
            else
            {
                result.Add(x);
            }
        }
        return result;
    }
}
