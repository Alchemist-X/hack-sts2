using System.Security.Cryptography;

namespace Sts2Sim.Diff.V2;

public static class SegmentKinds
{
    public const string Combat = "combat";
    public const string Room = "room";
    public const string Map = "map";

    public static bool IsKnown(string kind) => kind is Combat or Room or Map;
}

public static class Screens
{
    public const string Combat = "combat";
    public const string Map = "map";
    public const string Rewards = "rewards";
    public const string CardReward = "card_reward";
    public const string Shop = "shop";
    public const string RestSite = "rest_site";
    public const string Event = "event";
    public const string Treasure = "treasure";
    public const string Select = "select";
    public const string Victory = "victory";
    public const string Defeat = "defeat";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Combat, Map, Rewards, CardReward, Shop, RestSite, Event, Treasure, Select
    };
}

public sealed record BundleAnchor(string Id, string Kind, string? Ref, string? Sha256, int? Seq, int? Act, int? Floor);

public sealed class SegmentDecision
{
    public int Index { get; init; }

    public int? RunIndex { get; init; }

    public string Screen { get; init; } = Screens.Combat;

    public JsonNode? PreState { get; init; }

    public JsonNode? Oracle { get; init; }

    public required V2Action Action { get; init; }

    public JsonObject? RoomEntered { get; init; }

    public bool IsObserved => PreState != null;
}

public sealed class BundleSegment
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public int? Act { get; init; }

    public int? Floor { get; init; }

    public string? RoomType { get; init; }

    public string? RoomModelId { get; init; }

    public int? Attempt { get; init; }

    public string? StartAnchor { get; init; }

    public string? EndAnchor { get; init; }

    public JsonObject? Start { get; init; }

    public DiffBundle? CombatStart { get; set; }

    public IReadOnlyList<SegmentDecision> Decisions { get; init; } = Array.Empty<SegmentDecision>();

    public string? OutcomeResult { get; init; }

    public int? OutcomeFinalHp { get; init; }

    public JsonObject? FloorSummary { get; init; }

    public bool? Eligible { get; init; }

    public IReadOnlyList<string> EligibilityReasons { get; init; } = Array.Empty<string>();

    public string Sha256 { get; init; } = string.Empty;

    public string? FormatError { get; set; }

    // W4.1 mcr join (V2/McrJoin.cs): the decoded replay part the combat start was joined from, or why it was not.
    public string? McrJoinedPart { get; set; }

    public string? McrJoinNote { get; set; }

    /// <summary>
    /// Contract fields the exporter wrote as <c>null</c> on an ineligible segment because the recording does not carry
    /// them (diff-bundle-v2.md X3.5 <c>start.deck</c> under <c>recorder_gap:deck_order</c> /
    /// <c>deck_reconstruction_failed</c>, X4.6 <c>decisions[i].action</c> under <c>decision_unresolved</c>). Read as
    /// unavailable, not as a parse failure; the runner skips the segment with its eligibility reasons.
    /// </summary>
    public List<string> UnavailableFields { get; } = new();
}

public sealed class RunBundle
{
    public int BundleVersion { get; init; } = 2;

    public string Kind { get; init; } = "run";

    public bool FromV1 { get; init; }

    public string Lineage { get; init; } = "unknown";

    public JsonNode? SourceJson { get; init; }

    public string? Driver { get; init; }

    public string GameVersion { get; init; } = BundleRunner.UnknownVersion;

    public string? RulesVersion { get; init; }

    public string Seeding { get; init; } = RngSetup.SeedingXxh64;

    public string Seed { get; init; } = string.Empty;

    public string Character { get; init; } = StartingDecks.IroncladCharacterId;

    public int Ascension { get; init; }

    public string GameMode { get; init; } = "standard";

    public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<BundleAnchor> Anchors { get; init; } = Array.Empty<BundleAnchor>();

    public IReadOnlyList<BundleSegment> Segments { get; init; } = Array.Empty<BundleSegment>();

    public IReadOnlyList<string> RunReasons { get; init; } = Array.Empty<string>();

    public string? FilePath { get; init; }

    public IReadOnlyCollection<string> IgnoredKeys { get; init; } = Array.Empty<string>();

    public BundleAnchor? FindAnchor(string? id) => id == null ? null : Anchors.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}

public static class RunBundleJson
{
    public const int Version = 2;

    private static readonly string[] TopLevelKeys =
    {
        "bundle_version", "kind", "source", "game_version", "rules_version", "seeding", "seed", "character", "ascension",
        "game_mode", "modifiers", "anchors", "segments", "outcome", "eligibility"
    };

    private static readonly string[] SegmentKeys =
    {
        "id", "kind", "act", "floor", "room_type", "room_model_id", "attempt", "start_anchor", "end_anchor", "start",
        "decisions", "outcome", "floor_summary", "eligibility"
    };

    private static readonly string[] DecisionKeys = { "index", "run_index", "screen", "pre_state", "oracle", "action", "room_entered" };

    public static RunBundle ReadAny(JsonElement root, string? filePath)
    {
        var r = new JsonReadHelper(null);
        r.RequireObject(root, "$");
        int version = r.Int(root, "bundle_version", "$");
        return version switch
        {
            BundleJson.SupportedVersion => FromV1(BundleJson.Read(root, filePath), root.GetRawText()),
            Version => Read(root, filePath),
            _ => throw new BundleFormatException("$.bundle_version", $"unsupported bundle version {version} (supported: {BundleJson.SupportedVersion}, {Version})")
        };
    }

    public static RunBundle Parse(string json, string? filePath = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BundleFormatException("$", "invalid JSON: " + ex.Message);
        }
        using (doc)
        {
            return ReadAny(doc.RootElement, filePath);
        }
    }

    public static RunBundle FromV1(DiffBundle b, string? rawText = null)
    {
        ArgumentNullException.ThrowIfNull(b);
        var decisions = new List<SegmentDecision>(b.Decisions.Count);
        foreach (var d in b.Decisions)
        {
            decisions.Add(new SegmentDecision
            {
                Index = d.Index,
                Screen = Screens.Combat,
                PreState = d.PreStateJson?.DeepClone() ?? (d.PreState != null ? ProjectionJson.Write(d.PreState) : null),
                Oracle = d.OracleJson?.DeepClone(),
                Action = V2Action.FromV1(d.Action)
            });
        }
        string lineage = b.Source.Session ?? (b.FilePath != null ? Path.GetFileNameWithoutExtension(b.FilePath) : "v1");
        string segId = "combat" + (b.Source.Attempt is int attempt ? "-a" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        var segment = new BundleSegment
        {
            Id = segId,
            Kind = SegmentKinds.Combat,
            Act = b.Source.Act,
            Floor = b.Source.Floor,
            RoomModelId = b.EncounterId,
            Attempt = b.Source.Attempt,
            CombatStart = b,
            Decisions = decisions,
            OutcomeResult = b.Outcome,
            OutcomeFinalHp = b.FinalHp,
            EligibilityReasons = b.IneligibleReasons,
            Sha256 = Sha(rawText ?? BundleJson.Write(b).ToJsonString())
        };
        return new RunBundle
        {
            BundleVersion = BundleJson.SupportedVersion,
            Kind = SegmentKinds.Combat,
            FromV1 = true,
            Lineage = lineage,
            SourceJson = b.SourceJson?.DeepClone(),
            GameVersion = b.GameVersion,
            RulesVersion = b.GameVersion,
            Seeding = b.Seeding,
            Seed = b.Seed,
            Character = b.Character,
            Ascension = b.Ascension,
            Segments = new[] { segment },
            FilePath = b.FilePath,
            IgnoredKeys = b.IgnoredKeys
        };
    }

    public static RunBundle Read(JsonElement root, string? filePath = null)
    {
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        var r = new JsonReadHelper(ignored);
        r.RequireObject(root, "$");
        r.NoteUnknownKeys(root, "$", TopLevelKeys);
        int version = r.Int(root, "bundle_version", "$");
        if (version != Version)
        {
            throw new BundleFormatException("$.bundle_version", $"expected bundle version {Version}, got {version}");
        }
        string kind = r.OptionalString(root, "kind", "$") ?? "run";
        if (kind != "run" && kind != SegmentKinds.Combat)
        {
            throw new BundleFormatException("$.kind", $"unknown bundle kind '{kind}' (expected 'run' or 'combat')");
        }
        string? seedingOrNull = (!root.TryGetProperty("seeding", out var seedingEl) || seedingEl.ValueKind == JsonValueKind.Null)
            && root.TryGetProperty("seed", out var oldSeedEl) && oldSeedEl.ValueKind == JsonValueKind.String && RngSetup.HasOldPrefix(oldSeedEl.GetString()!)
            ? UnknownSeeding
            : null;
        string seeding = seedingOrNull ?? r.String(root, "seeding", "$");
        if (seeding != RngSetup.SeedingXxh64 && seeding != RngSetup.SeedingLegacyV18 && seeding != UnknownSeeding)
        {
            throw new BundleFormatException("$.seeding", $"unknown seeding '{seeding}'");
        }

        string lineage = "unknown";
        string? driver = null;
        JsonNode? sourceJson = null;
        if (r.TryProperty(root, "source", out var sourceEl))
        {
            r.RequireObject(sourceEl, "$.source");
            sourceJson = JsonNode.Parse(sourceEl.GetRawText());
            lineage = r.OptionalString(sourceEl, "lineage", "$.source") ?? lineage;
            if (r.TryProperty(sourceEl, "provenance", out var prov) && prov.ValueKind == JsonValueKind.Object)
            {
                driver = r.OptionalString(prov, "driver", "$.source.provenance");
            }
        }
        if (lineage == "unknown" && filePath != null)
        {
            lineage = Path.GetFileNameWithoutExtension(filePath);
        }

        var modifiers = new List<string>();
        if (r.TryProperty(root, "modifiers", out var modEl))
        {
            r.RequireArray(modEl, "$.modifiers");
            int mi = 0;
            foreach (var m in modEl.EnumerateArray())
            {
                modifiers.Add(m.ValueKind == JsonValueKind.String ? m.GetString()! : m.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString()! : throw new BundleFormatException($"$.modifiers[{mi}]", "expected a string or an object with 'id'"));
                mi++;
            }
        }

        var anchors = new List<BundleAnchor>();
        if (r.TryProperty(root, "anchors", out var anchorsEl))
        {
            r.RequireArray(anchorsEl, "$.anchors");
            int ai = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in anchorsEl.EnumerateArray())
            {
                string p = $"$.anchors[{ai}]";
                r.RequireObject(a, p);
                r.NoteUnknownKeys(a, p, "id", "kind", "seq", "act", "floor", "ref", "sha256");
                string id = r.String(a, "id", p);
                if (!seen.Add(id))
                {
                    throw new BundleFormatException(p + ".id", $"duplicate anchor id '{id}'");
                }
                anchors.Add(new BundleAnchor(id, r.String(a, "kind", p), r.OptionalString(a, "ref", p), r.OptionalString(a, "sha256", p),
                    r.OptionalInt(a, "seq", p), r.OptionalInt(a, "act", p), r.OptionalInt(a, "floor", p)));
                ai++;
            }
        }

        var segments = new List<BundleSegment>();
        var segmentsEl = r.Property(root, "segments", "$");
        r.RequireArray(segmentsEl, "$.segments");
        int si = 0;
        var segIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in segmentsEl.EnumerateArray())
        {
            string p = $"$.segments[{si}]";
            BundleSegment segment;
            try
            {
                segment = ReadSegment(r, s, p);
            }
            catch (BundleFormatException ex) when (MarkedIneligible(s))
            {
                segment = UnreadableSegment(s, si, ex.Message);
            }
            if (!segIds.Add(segment.Id))
            {
                throw new BundleFormatException(p + ".id", $"duplicate segment id '{segment.Id}'");
            }
            foreach (var anchorId in new[] { segment.StartAnchor, segment.EndAnchor })
            {
                if (anchorId != null && !anchors.Any(a => a.Id == anchorId))
                {
                    throw new BundleFormatException(p, $"anchor '{anchorId}' is not listed in $.anchors");
                }
            }
            segments.Add(segment);
            si++;
        }

        var runReasons = new List<string>();
        if (r.TryProperty(root, "eligibility", out var eligEl) && eligEl.ValueKind == JsonValueKind.Object)
        {
            runReasons.AddRange(r.OptionalStringList(eligEl, "run_reasons", "$.eligibility") ?? new List<string>());
        }

        var bundle = new RunBundle
        {
            BundleVersion = version,
            Kind = kind,
            Lineage = lineage,
            SourceJson = sourceJson,
            Driver = driver,
            GameVersion = r.String(root, "game_version", "$"),
            RulesVersion = r.OptionalString(root, "rules_version", "$"),
            Seeding = seeding,
            Seed = r.String(root, "seed", "$"),
            Character = r.String(root, "character", "$"),
            Ascension = r.Int(root, "ascension", "$"),
            GameMode = r.OptionalString(root, "game_mode", "$") ?? "standard",
            Modifiers = modifiers,
            Anchors = anchors,
            Segments = segments,
            RunReasons = runReasons,
            FilePath = filePath,
            IgnoredKeys = ignored
        };
        foreach (var segment in segments)
        {
            string? mismatch = null;
            if (segment.FormatError == null && segment.UnavailableFields.Count == 0 && McrJoin.TryJoin(bundle, segment, out mismatch) is { } joined)
            {
                try
                {
                    // The join's reasons replace the segment's (anchor_* closed); an otherwise clean joined start is eligible.
                    segment.CombatStart = BuildCombatStart(bundle, segment, $"$.segments[{segments.IndexOf(segment)}].start", joined.Start, joined.Reasons, markedIneligible: false);
                    segment.McrJoinedPart = joined.Part;
                    continue;
                }
                catch (BundleFormatException ex)
                {
                    segment.McrJoinNote = "mcr_join_unreadable: " + ex.Message;
                }
            }
            else if (mismatch != null)
            {
                segment.McrJoinNote = mismatch;
            }
            if (segment.FormatError == null && segment.UnavailableFields.Count == 0 && segment.Kind == SegmentKinds.Combat && segment.Start != null && IsV1ShapedStart(segment.Start))
            {
                try
                {
                    segment.CombatStart = BuildCombatStart(bundle, segment, $"$.segments[{segments.IndexOf(segment)}].start");
                }
                catch (BundleFormatException ex) when (segment.Eligible == false)
                {
                    segment.FormatError = ex.Message;
                }
            }
        }
        return bundle;
    }

    public const string UnknownSeeding = "unknown";

    private static bool MarkedIneligible(JsonElement s)
        => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("eligibility", out var e) && e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("eligible", out var el) && el.ValueKind == JsonValueKind.False;

    private static BundleSegment UnreadableSegment(JsonElement s, int index, string error)
    {
        string? Str(string name) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool? eligible = null;
        var reasons = new List<string>();
        if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("eligibility", out var e) && e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("eligible", out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                eligible = el.GetBoolean();
            }
            if (e.TryGetProperty("reasons", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                reasons.AddRange(rs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            }
        }
        string kind = Str("kind") ?? "unknown";
        return new BundleSegment
        {
            Id = Str("id") ?? $"segment{index}",
            Kind = SegmentKinds.IsKnown(kind) ? kind : "unknown",
            Eligible = eligible,
            EligibilityReasons = reasons,
            Sha256 = Sha(s.GetRawText()),
            FormatError = error
        };
    }

    public static bool IsV1ShapedStart(JsonObject start) => start.ContainsKey("deck") && start.ContainsKey("player");

    private static BundleSegment ReadSegment(JsonReadHelper r, JsonElement s, string p)
    {
        r.RequireObject(s, p);
        r.NoteUnknownKeys(s, p, SegmentKeys);
        string kind = r.String(s, "kind", p);
        if (!SegmentKinds.IsKnown(kind))
        {
            throw new BundleFormatException(p + ".kind", $"unknown segment kind '{kind}'");
        }
        JsonObject? start = null;
        if (r.TryProperty(s, "start", out var startEl))
        {
            r.RequireObject(startEl, p + ".start");
            start = JsonNode.Parse(startEl.GetRawText())!.AsObject();
        }
        var unavailable = new List<string>();
        if (start != null && kind == SegmentKinds.Combat && start.ContainsKey("deck") && start["deck"] == null && MarkedIneligible(s))
        {
            unavailable.Add("start.deck");
        }
        var decisions = new List<SegmentDecision>();
        var decisionsEl = r.Property(s, "decisions", p);
        r.RequireArray(decisionsEl, p + ".decisions");
        int i = 0;
        foreach (var d in decisionsEl.EnumerateArray())
        {
            string dp = $"{p}.decisions[{i}]";
            r.RequireObject(d, dp);
            r.NoteUnknownKeys(d, dp, DecisionKeys);
            int index = r.Int(d, "index", dp);
            if (index != i)
            {
                throw new BundleFormatException(dp + ".index", $"decision index {index} does not match its position {i}");
            }
            string screen = r.OptionalString(d, "screen", dp) ?? (kind == SegmentKinds.Combat ? Screens.Combat : throw new BundleFormatException(dp, "missing required field 'screen'"));
            if (!Screens.Known.Contains(screen))
            {
                throw new BundleFormatException(dp + ".screen", $"unknown screen '{screen}'");
            }
            V2Action action;
            if (d.TryGetProperty("action", out var rawAction) && rawAction.ValueKind == JsonValueKind.Null && MarkedIneligible(s))
            {
                action = new V2Action(V2Action.Unavailable, new JsonObject { ["type"] = V2Action.Unavailable });
                unavailable.Add("decisions[" + i + "].action");
            }
            else
            {
                action = V2Action.Read(r, r.Property(d, "action", dp), dp + ".action");
            }
            if (kind == SegmentKinds.Combat && action.Type != V2Action.Unavailable && !V2Action.CombatTypes.Contains(action.Type))
            {
                throw new BundleFormatException(dp + ".action.type", $"action '{action.Type}' is not a combat action");
            }
            JsonObject? roomEntered = null;
            if (r.TryProperty(d, "room_entered", out var re))
            {
                r.RequireObject(re, dp + ".room_entered");
                roomEntered = JsonNode.Parse(re.GetRawText())!.AsObject();
            }
            decisions.Add(new SegmentDecision
            {
                Index = index,
                RunIndex = r.OptionalInt(d, "run_index", dp),
                Screen = screen,
                PreState = r.TryProperty(d, "pre_state", out var pre) ? JsonNode.Parse(pre.GetRawText()) : null,
                Oracle = r.TryProperty(d, "oracle", out var oracle) ? JsonNode.Parse(oracle.GetRawText()) : null,
                Action = action,
                RoomEntered = roomEntered
            });
            i++;
        }
        string? result = null;
        int? finalHp = null;
        if (r.TryProperty(s, "outcome", out var outEl))
        {
            r.RequireObject(outEl, p + ".outcome");
            result = r.OptionalString(outEl, "result", p + ".outcome");
            finalHp = r.OptionalInt(outEl, "final_hp", p + ".outcome");
            if (result != null && result is not ("win" or "loss" or "abandoned" or "left"))
            {
                throw new BundleFormatException(p + ".outcome.result", $"unknown outcome '{result}'");
            }
        }
        bool? eligible = null;
        var reasons = new List<string>();
        if (r.TryProperty(s, "eligibility", out var eligEl))
        {
            r.RequireObject(eligEl, p + ".eligibility");
            if (r.TryProperty(eligEl, "eligible", out _))
            {
                eligible = r.Bool(eligEl, "eligible", p + ".eligibility");
            }
            reasons.AddRange(r.OptionalStringList(eligEl, "reasons", p + ".eligibility") ?? new List<string>());
        }
        JsonObject? floorSummary = null;
        if (r.TryProperty(s, "floor_summary", out var fs))
        {
            r.RequireObject(fs, p + ".floor_summary");
            floorSummary = JsonNode.Parse(fs.GetRawText())!.AsObject();
        }
        var segment = new BundleSegment
        {
            Id = r.String(s, "id", p),
            Kind = kind,
            Act = r.OptionalInt(s, "act", p),
            Floor = r.OptionalInt(s, "floor", p),
            RoomType = r.OptionalString(s, "room_type", p),
            RoomModelId = r.OptionalString(s, "room_model_id", p),
            Attempt = r.OptionalInt(s, "attempt", p),
            StartAnchor = r.OptionalString(s, "start_anchor", p),
            EndAnchor = r.OptionalString(s, "end_anchor", p),
            Start = start,
            Decisions = decisions,
            OutcomeResult = result,
            OutcomeFinalHp = finalHp,
            FloorSummary = floorSummary,
            Eligible = eligible,
            EligibilityReasons = reasons,
            Sha256 = Sha(s.GetRawText())
        };
        segment.UnavailableFields.AddRange(unavailable);
        return segment;
    }

    private static DiffBundle BuildCombatStart(RunBundle run, BundleSegment segment, string path)
        => BuildCombatStart(run, segment, path, segment.Start!, segment.EligibilityReasons, segment.Eligible == false);

    private static DiffBundle BuildCombatStart(RunBundle run, BundleSegment segment, string path, JsonObject start, IReadOnlyList<string> eligibilityReasons, bool markedIneligible)
    {
        string? encounter = start["encounter_id"]?.GetValue<string>() ?? segment.RoomModelId;
        if (encounter == null)
        {
            throw new BundleFormatException(path, "a v1-shaped combat start needs 'encounter_id' or the segment's 'room_model_id'");
        }
        var source = new JsonObject { ["session"] = run.Lineage };
        if (segment.Attempt is int attempt) source["attempt"] = attempt;
        if (segment.Act is int act) source["act"] = act;
        if (segment.Floor is int floor) source["floor"] = floor;
        var shell = new JsonObject
        {
            ["bundle_version"] = BundleJson.SupportedVersion,
            ["source"] = source,
            ["game_version"] = run.GameVersion,
            ["seeding"] = run.Seeding,
            ["seed"] = run.Seed,
            ["character"] = run.Character,
            ["ascension"] = run.Ascension,
            ["encounter_id"] = encounter,
            ["player"] = start["player"]?.DeepClone(),
            ["deck"] = start["deck"]?.DeepClone(),
            ["relics"] = start["relics"]?.DeepClone() ?? new JsonArray(),
            ["rng_offsets"] = start["rng_offsets"]?.DeepClone() ?? new JsonObject(),
            ["decisions"] = new JsonArray(),
            ["outcome"] = DiffBundle.OutcomeAbandoned
        };
        if (eligibilityReasons.Count > 0 || markedIneligible)
        {
            var reasons = new JsonArray();
            foreach (var reason in eligibilityReasons.DefaultIfEmpty("segment marked ineligible"))
            {
                reasons.Add(reason);
            }
            shell[BundleJson.IneligibleReasonsKey] = reasons;
        }
        using var doc = JsonDocument.Parse(shell.ToJsonString());
        try
        {
            return BundleJson.Read(doc.RootElement, run.FilePath);
        }
        catch (BundleFormatException ex)
        {
            throw new BundleFormatException(path, "v1-shaped start: " + ex.Message);
        }
    }

    public static IReadOnlyList<CombatStartPotion> StartPotions(BundleSegment segment)
    {
        var list = new List<CombatStartPotion>();
        if (segment.Start?["potions"] is JsonArray arr)
        {
            int i = 0;
            foreach (var node in arr)
            {
                if (node is JsonObject o && o["id"] is JsonValue idv && idv.TryGetValue(out string? id) && id != null)
                {
                    int slot = o["slot"] is JsonValue sv && sv.TryGetValue(out int s) ? s : i;
                    list.Add(new CombatStartPotion(slot, id));
                }
                i++;
            }
        }
        return list;
    }

    public static string Sha(string text)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public readonly record struct CombatStartPotion(int Slot, string Id);
