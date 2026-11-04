namespace Sts2Sim.Diff.V2;

// W4.1 mcr join (sim/specs/diff-report-w4-combat.md W4.1, experiment B of §4): a v2 combat segment whose start anchor
// is an `mcr_initial` anchor gets the decoded replay's run-RNG counters (`rng_offsets`), deck (save order) and relics
// from the replay-convert output (`<bundle dir>/mcr/{eligible,ineligible}/<part>.json`), provided the decoded start
// and the segment's first battle snapshot agree on encounter, floor, relics, deck multiset and HP. Reasons the join
// closes (`anchor_*`) are dropped; the decoded bundle's start-shape reasons are kept with an `mcr:` prefix.
public static class McrJoin
{
    public const string ReasonPrefix = "mcr:";

    // The joined deck is the save's deck order, which closes the exporter's deck-order gap.
    public const string DeckOrderGap = "recorder_gap:deck_order";

    public static readonly string[] KeptMcrReasons = { "enchanted_card_in_deck", "model_table_unverified", "encounter_unresolved", "multiplayer" };

    public sealed record Joined(JsonObject Start, IReadOnlyList<string> Reasons, string Part);

    public static string? FindDecodedBundle(RunBundle run, BundleAnchor anchor)
    {
        if (anchor.Ref == null || run.FilePath == null)
        {
            return null;
        }
        string part = anchor.Ref.Split('/')[0];
        string dir = Path.GetDirectoryName(Path.GetFullPath(run.FilePath)) ?? ".";
        foreach (var sub in new[] { "eligible", "ineligible" })
        {
            string path = Path.Combine(dir, "mcr", sub, part + ".json");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    // Returns null when the segment has no mcr_initial anchor or no decoded replay; `mismatch` is set when a decoded
    // replay exists but does not describe the segment's start.
    public static Joined? TryJoin(RunBundle run, BundleSegment segment, out string? mismatch)
    {
        mismatch = null;
        if (segment.Kind != SegmentKinds.Combat || segment.Start == null)
        {
            return null;
        }
        var anchor = run.FindAnchor(segment.StartAnchor);
        if (anchor == null || anchor.Kind != AnchorLoader.AnchorMcrInitial)
        {
            return null;
        }
        string? path = FindDecodedBundle(run, anchor);
        if (path == null)
        {
            return null;
        }
        JsonObject m;
        try
        {
            m = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            mismatch = $"mcr_join_unreadable: {Path.GetFileName(path)}: {ex.Message}";
            return null;
        }
        mismatch = Mismatch(m, segment);
        if (mismatch != null)
        {
            mismatch = "mcr_join_mismatch: " + mismatch;
            return null;
        }
        var start = (JsonObject)segment.Start.DeepClone();
        start["rng_offsets"] = m["rng_offsets"]?.DeepClone() ?? new JsonObject();
        start["deck"] = m["deck"]?.DeepClone();
        start["relics"] = m["relics"]?.DeepClone() ?? new JsonArray();
        if (m[BundleJson.RelicPropsKey] is JsonArray relicProps)
        {
            start[BundleJson.RelicPropsKey] = relicProps.DeepClone();
        }
        start.Remove("anchor");
        var reasons = segment.EligibilityReasons
            .Where(r => !r.StartsWith("anchor_", StringComparison.Ordinal) && !string.Equals(r, DeckOrderGap, StringComparison.Ordinal))
            .ToList();
        if (m["ineligible_reasons"] is JsonArray mr)
        {
            foreach (var r in mr)
            {
                string? s = r?.GetValue<string>();
                if (s != null && KeptMcrReasons.Any(k => s.StartsWith(k, StringComparison.Ordinal)))
                {
                    reasons.Add(ReasonPrefix + s);
                }
            }
        }
        return new Joined(start, reasons, Path.GetFileNameWithoutExtension(path));
    }

    private static string? Mismatch(JsonObject m, BundleSegment segment)
    {
        var st = segment.Start!;
        string? enc = m["encounter_id"]?.GetValue<string>();
        string? segEnc = st["encounter_id"]?.GetValue<string>();
        if (!string.Equals(enc, segEnc, StringComparison.Ordinal))
        {
            return $"encounter {enc} != {segEnc}";
        }
        int? floor = m["source"]?["floor"] is JsonValue fv && fv.TryGetValue(out int f) ? f : null;
        if (floor != segment.Floor)
        {
            return $"floor {floor} != {segment.Floor}";
        }
        var relics = Strings(m["relics"]).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var segRelics = Strings(st["relics"]).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (!relics.SequenceEqual(segRelics))
        {
            return "relics differ [" + string.Join(", ", relics.Except(segRelics).Concat(segRelics.Except(relics))) + "]";
        }
        if (st["deck_multiset"] is JsonArray multiset && multiset.Count > 0)
        {
            var deck = (m["deck"] as JsonArray ?? new JsonArray())
                .Select(c => c!["id"]!.GetValue<string>() + (c["upgraded"]?.GetValue<bool>() == true ? "+" : ""))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            var segDeck = Strings(multiset).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!deck.SequenceEqual(segDeck))
            {
                return "deck multiset differs";
            }
        }
        if (!JsonNode.DeepEquals(m["player"], st["player"]))
        {
            return $"hp {m["player"]?.ToJsonString()} != {st["player"]?.ToJsonString()}";
        }
        return null;
    }

    private static IEnumerable<string> Strings(JsonNode? node)
        => node is JsonArray a ? a.Select(x => x?.GetValue<string>()).Where(x => x != null).Select(x => x!) : Enumerable.Empty<string>();
}
