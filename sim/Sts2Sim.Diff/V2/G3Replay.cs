namespace Sts2Sim.Diff.V2;

public sealed class G3Prepared
{
    public required JsonObject Bundle { get; init; }

    public string OriginalGameMode { get; init; } = "standard";

    public List<string> DroppedAttempts { get; } = new();

    public string? TruncatedAt { get; set; }

    public string? TruncationReason { get; set; }

    public int SegmentsInBundle { get; init; }

    public int ResolvedHandPicks { get; set; }

    public int ReorderedHandSelects { get; set; }

    public int DroppedFinalHp { get; set; }
}

public sealed class G3RunResult
{
    public required string Lineage { get; init; }

    public required string File { get; init; }

    public string GameVersion { get; init; } = BundleRunner.UnknownVersion;

    public string Character { get; init; } = string.Empty;

    public int Ascension { get; init; }

    public string GameMode { get; init; } = "standard";

    public string? RecordedResult { get; init; }

    public int? RecordedFloor { get; init; }

    public G3Prepared? Prepared { get; init; }

    public List<SegmentResult> Segments { get; } = new();

    public Dictionary<string, BundleSegment> SegmentById { get; } = new(StringComparer.Ordinal);

    public string? Error { get; set; }

    public G3Loop.Counters Counters { get; } = new();
}

public static class G3Replay
{
    public const string Usage = "usage: Sts2Sim.Diff g3 <bundle-dir-or-file> [--report out.json] [--versions v0.111.0|all] [-v|--verbose]";

    public static G3Prepared Prepare(JsonObject source)
    {
        var bundle = source.DeepClone().AsObject();
        string mode = bundle["game_mode"]?.GetValue<string>() ?? "standard";
        bool noModifiers = bundle["modifiers"] is not JsonArray mods || mods.Count == 0;
        if (mode == "custom" && noModifiers)
        {
            bundle["game_mode"] = "standard";
        }
        var segments = bundle["segments"]!.AsArray();
        var maxAttempt = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in segments)
        {
            string key = AttemptKey(s!.AsObject());
            int attempt = s["attempt"]?.GetValue<int>() ?? 1;
            maxAttempt[key] = Math.Max(maxAttempt.TryGetValue(key, out int m) ? m : 0, attempt);
        }
        var prepared = new G3Prepared { Bundle = bundle, OriginalGameMode = mode, SegmentsInBundle = segments.Count };
        var kept = new JsonArray();
        bool first = true;
        foreach (var node in segments)
        {
            var s = node!.AsObject();
            string key = AttemptKey(s);
            int attempt = s["attempt"]?.GetValue<int>() ?? 1;
            string id = s["id"]?.GetValue<string>() ?? "?";
            if (attempt < maxAttempt[key] || SupersededByResume(segments, s))
            {
                prepared.DroppedAttempts.Add(id);
                continue;
            }
            var copy = s.DeepClone().AsObject();
            copy.Remove("start");
            copy.Remove("start_anchor");
            copy.Remove("end_anchor");
            if (first)
            {
                copy["start"] = new JsonObject { ["from"] = AnchorLoader.FromSeed, ["content"] = "all", ["neow"] = true };
                first = false;
            }
            if (copy["kind"]?.GetValue<string>() == SegmentKinds.Combat && copy["outcome"] is JsonObject outcome && outcome.Remove("final_hp"))
            {
                prepared.DroppedFinalHp++;
            }
            var decisions = copy["decisions"]!.AsArray();
            foreach (var dn in decisions)
            {
                if (copy["kind"]?.GetValue<string>() == SegmentKinds.Combat && dn?["pre_state"]?["select"] is JsonObject sel)
                {
                    var cands = new JsonArray();
                    foreach (var c in (sel["candidates"] as JsonArray) ?? new JsonArray())
                    {
                        cands.Add(new JsonObject { ["id"] = c?["id"]?.DeepClone(), ["upgraded"] = c?["upgraded"]?.DeepClone() });
                    }
                    dn["pre_state"]!["select"] = new JsonObject { ["kind"] = sel["kind"]?.DeepClone(), ["candidates"] = cands };
                    if (ReorderHandSelect(dn as JsonObject))
                    {
                        prepared.ReorderedHandSelects++;
                    }
                }
                if (dn?["action"] is JsonObject act && act["type"]?.GetValue<string>() == V2Action.RewardTake
                    && act["reward_type"] is JsonValue rt && rt.TryGetValue(out string? rtype) && rtype != null && rtype.EndsWith("_reward", StringComparison.Ordinal))
                {
                    act["reward_type"] = rtype[..^"_reward".Length];
                }
            }
            for (int i = 0; i + 1 < decisions.Count; i++)
            {
                if (ResolveHandPick(decisions[i] as JsonObject, decisions[i + 1] as JsonObject))
                {
                    prepared.ResolvedHandPicks++;
                }
                if (decisions[i]?["oracle"]?["recorded_order"]?.GetValue<string>() == "after_pick"
                    && decisions[i]?["action"]?["type"]?.GetValue<string>() == V2Action.RewardTake
                    && decisions[i + 1]?["action"]?["type"]?.GetValue<string>() is V2Action.CardRewardPick or V2Action.CardRewardSkip
                    && decisions[i + 1]?["pre_state"] is JsonObject before)
                {
                    decisions[i]!["pre_state"] = before.DeepClone();
                }
            }
            int cut = -1;
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i] is not JsonObject d || d["action"] == null)
                {
                    cut = i;
                    break;
                }
            }
            if (cut >= 0)
            {
                var d = decisions[cut]!.AsObject();
                string why = d["oracle"]?["unresolved_reason"]?.GetValue<string>()
                    ?? d["oracle"]?["status"]?.GetValue<string>()
                    ?? "no action";
                while (decisions.Count > cut)
                {
                    decisions.RemoveAt(decisions.Count - 1);
                }
                copy.Remove("floor_summary");
                copy.Remove("outcome");
                prepared.TruncatedAt = $"{id}#{cut}";
                prepared.TruncationReason = "decision_unresolved: " + why;
                kept.Add(copy);
                break;
            }
            kept.Add(copy);
        }
        bundle["segments"] = kept;
        bundle["anchors"] = new JsonArray();
        return prepared;
    }

    private static bool SupersededByResume(JsonArray segments, JsonObject s)
    {
        if (s["phase"]?.GetValue<string>() != "post_combat")
        {
            return false;
        }
        string key = AttemptKey(s);
        for (int i = segments.IndexOf(s) + 1; i < segments.Count; i++)
        {
            var next = segments[i]!.AsObject();
            if (next["kind"]?.GetValue<string>() != SegmentKinds.Room || AttemptKey(next) != key)
            {
                return false;
            }
            if (next["phase"]?.GetValue<string>() == "post_combat_resume")
            {
                return true;
            }
        }
        return false;
    }

    private static string CardKey(JsonNode? c) => (c?["id"]?.GetValue<string>() ?? "?") + (c?["upgraded"] is JsonValue u && u.TryGetValue(out bool up) && up ? "+" : "");

    /// <summary>
    /// recorder_gap: the recorder lists hand-select candidates in UI holder order (<c>NPlayerHand.ActiveHolders</c>,
    /// mod/src/StateBuilder/PassiveStateBuilder.Combat.cs), which can differ from the model hand order the game's
    /// <c>CardSelectCmd.FromHand</c> filters (Commands/CardSelectCmd.cs). When the candidates are a sub-multiset of the
    /// pre_state hand, put them in hand order and remap the pick indices to the same cards.
    /// </summary>
    public static bool ReorderHandSelect(JsonObject? d)
    {
        if (d?["pre_state"]?["select"]?["candidates"] is not JsonArray cands || d["pre_state"]?["player"]?["hand"] is not JsonArray hand || cands.Count < 2)
        {
            return false;
        }
        var c = cands.Select(CardKey).ToList();
        var h = hand.Select(CardKey).ToList();
        var used = new bool[h.Count];
        var handPos = new int[c.Count];
        for (int k = 0; k < c.Count; k++)
        {
            int at = -1;
            for (int j = 0; j < h.Count; j++)
            {
                if (!used[j] && string.Equals(h[j], c[k], StringComparison.Ordinal))
                {
                    at = j;
                    break;
                }
            }
            if (at < 0)
            {
                return false;
            }
            used[at] = true;
            handPos[k] = at;
        }
        var order = Enumerable.Range(0, c.Count).OrderBy(k => handPos[k]).ToList();
        if (order.SequenceEqual(Enumerable.Range(0, c.Count)))
        {
            return false;
        }
        var reordered = new JsonArray(order.Select(k => cands[k]!.DeepClone()).ToArray());
        d["pre_state"]!["select"]!["candidates"] = reordered;
        if (d["action"]?["picks"] is JsonArray picks)
        {
            foreach (var p in picks.OfType<JsonObject>())
            {
                if (p["index"] is JsonValue iv && iv.TryGetValue(out int idx) && idx >= 0 && idx < c.Count)
                {
                    p["index"] = order.IndexOf(idx);
                }
            }
        }
        return true;
    }

    public static bool ResolveHandPick(JsonObject? d, JsonObject? next)
    {
        if (d?["action"] is not JsonObject a || a["type"]?.GetValue<string>() != V2Action.Choose || a["kind"]?.GetValue<string>() != "hand")
        {
            return false;
        }
        if (a["picks"] is not JsonArray picks || picks.Count != 1 || picks[0] is not JsonObject pick)
        {
            return false;
        }
        if (d["pre_state"]?["select"]?["candidates"] is not JsonArray cands || next?["pre_state"]?["player"]?["hand"] is not JsonArray hand)
        {
            return false;
        }
        var c = cands.Select(CardKey).ToList();
        var h = hand.Select(CardKey).ToList();
        if (h.Count != c.Count - 1)
        {
            return false;
        }
        int current = pick["index"] is JsonValue iv && iv.TryGetValue(out int idx) ? idx : -1;
        var matches = new List<int>();
        for (int k = 0; k < c.Count; k++)
        {
            var rest = c.Take(k).Concat(c.Skip(k + 1));
            if (rest.SequenceEqual(h, StringComparer.Ordinal))
            {
                matches.Add(k);
            }
        }
        if (matches.Count == 0 || matches.Contains(current))
        {
            return false;
        }
        if (pick["card_id"]?.GetValue<string>() is { } id && !string.Equals(c[matches[0]].TrimEnd('+'), id, StringComparison.Ordinal))
        {
            return false;
        }
        pick["index"] = matches[0];
        return true;
    }

    private static string AttemptKey(JsonObject s)
    {
        return string.Join("|", s["act"]?.ToJsonString() ?? "-", s["floor"]?.ToJsonString() ?? "-", s["room_type"]?.ToJsonString() ?? "-", s["room_model_id"]?.ToJsonString() ?? "-");
    }

    public static G3RunResult RunFile(string path)
    {
        var raw = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var result = new G3RunResult
        {
            Lineage = raw["source"]?["lineage"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path),
            File = Path.GetFileName(path),
            GameVersion = raw["game_version"]?.GetValue<string>() ?? BundleRunner.UnknownVersion,
            Character = raw["character"]?.GetValue<string>() ?? string.Empty,
            Ascension = raw["ascension"]?.GetValue<int>() ?? 0,
            GameMode = raw["game_mode"]?.GetValue<string>() ?? "standard",
            RecordedResult = raw["outcome"]?["result"]?.GetValue<string>(),
            RecordedFloor = raw["outcome"]?["floor"]?.GetValue<int>(),
            Prepared = Prepare(raw)
        };
        RunBundle run;
        try
        {
            run = RunBundleJson.Parse(result.Prepared.Bundle.ToJsonString(), path);
        }
        catch (BundleFormatException ex)
        {
            result.Error = "bundle format: " + ex.Message;
            return result;
        }
        foreach (var s in run.Segments)
        {
            result.SegmentById[s.Id] = s;
        }
        result.Segments.AddRange(G3Loop.Run(run, result.Counters));
        return result;
    }

    public static int RunMain(string[] args)
    {
        string? input = null;
        string? reportPath = null;
        string versions = "v0.111.0";
        bool verbose = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--report" or "--versions")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine(Usage);
                    return 2;
                }
                if (arg == "--report") reportPath = args[++i];
                else versions = args[++i];
            }
            else if (arg is "-v" or "--verbose")
            {
                verbose = true;
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
        var wanted = versions == "all" ? null : new HashSet<string>(versions.Split(','), StringComparer.Ordinal);
        string? reportFull = reportPath != null ? Path.GetFullPath(reportPath) : null;
        var files = Program.DiscoverFiles(input, reportFull);
        var runs = new List<G3RunResult>();
        foreach (var f in files)
        {
            JsonNode? head;
            try
            {
                head = JsonNode.Parse(File.ReadAllText(f));
            }
            catch (JsonException)
            {
                continue;
            }
            if (head is not JsonObject o || o["bundle_version"]?.GetValue<int>() != 2 || o["kind"]?.GetValue<string>() != "run")
            {
                continue;
            }
            string version = o["game_version"]?.GetValue<string>() ?? BundleRunner.UnknownVersion;
            if (wanted != null && !wanted.Contains(version))
            {
                continue;
            }
            var r = RunFile(f);
            runs.Add(r);
            Console.WriteLine(G3Report.Line(r));
            if (verbose && G3Report.FirstDivergence(r) is { } fd && fd.Divergence != null)
            {
                Console.WriteLine("    oracle: " + fd.Divergence.OracleProjection?.ToJsonString());
                Console.WriteLine("    sim:    " + fd.Divergence.SimProjection?.ToJsonString());
            }
        }
        var report = G3Report.Build(runs);
        Console.WriteLine(report["summary"]!.ToJsonString());
        if (reportFull != null)
        {
            File.WriteAllText(reportFull, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("report: " + reportFull);
        }
        return 0;
    }
}
