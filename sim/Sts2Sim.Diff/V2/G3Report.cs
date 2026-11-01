namespace Sts2Sim.Diff.V2;

public static class G3Report
{
    public static SegmentResult? FirstDivergence(G3RunResult r) => r.Segments.FirstOrDefault(s => s.Status != BundleStatus.Agree);

    public static int FloorsAgreed(G3RunResult r)
    {
        var fd = FirstDivergence(r);
        if (fd == null)
        {
            var last = r.Segments.Count > 0 && r.SegmentById.TryGetValue(r.Segments[^1].SegmentId, out var ls) ? ls.Floor ?? 0 : 0;
            return last;
        }
        return r.SegmentById.TryGetValue(fd.SegmentId, out var seg) ? Math.Max(0, (seg.Floor ?? 1) - 1) : 0;
    }

    public static bool ReachedEnd(G3RunResult r) => r.Error == null && r.Prepared?.TruncatedAt == null && FirstDivergence(r) == null && r.Segments.Count > 0;

    public static string Line(G3RunResult r)
    {
        if (r.Error != null)
        {
            return $"{r.Lineage}: ERROR {r.Error}";
        }
        var fd = FirstDivergence(r);
        int agreed = r.Segments.Count(s => s.Status == BundleStatus.Agree);
        string head = $"{r.Lineage} [{r.GameVersion} {r.Character} A{r.Ascension} {r.GameMode}]: {agreed}/{r.Segments.Count} segments agree, floors agreed {FloorsAgreed(r)}/{r.RecordedFloor}";
        if (fd == null)
        {
            return head + (r.Prepared?.TruncatedAt != null ? $", truncated at {r.Prepared.TruncatedAt} ({r.Prepared.TruncationReason})" : ", reached the recorded end");
        }
        var seg = r.SegmentById.GetValueOrDefault(fd.SegmentId);
        var first = fd.Divergence?.First;
        string where = seg != null ? $"{fd.SegmentId} {seg.Kind} f{seg.Floor} {seg.RoomType}/{seg.RoomModelId}" : fd.SegmentId;
        string detail = first != null ? $" field {first.Path}: oracle {first.Oracle?.ToJsonString() ?? "null"} sim {first.Sim?.ToJsonString() ?? "null"}{(first.Note != null ? " (" + first.Note + ")" : "")}" : "";
        return head + $"; first divergence {where} [{fd.Status} {fd.Class}] {fd.Reason}{detail}";
    }

    public static JsonObject Build(List<G3RunResult> runs)
    {
        var runArray = new JsonArray();
        var distribution = new JsonObject();
        var byClass = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var areaRuns = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int reached = 0;
        int compared = 0;
        int agreedDecisions = 0;
        foreach (var r in runs)
        {
            var fd = FirstDivergence(r);
            int floors = FloorsAgreed(r);
            distribution[r.Lineage] = floors;
            if (ReachedEnd(r))
            {
                reached++;
            }
            string cls = r.Error != null ? DivergenceClass.ExporterBug : fd?.Class ?? (r.Prepared?.TruncatedAt != null ? "truncated" : "none");
            byClass[cls] = byClass.GetValueOrDefault(cls) + 1;
            var areas = CertifiedAreas(r);
            foreach (var a in areas)
            {
                if (!areaRuns.TryGetValue(a, out var set))
                {
                    areaRuns[a] = set = new SortedSet<string>(StringComparer.Ordinal);
                }
                set.Add(r.Lineage);
            }
            int c = r.Segments.Sum(s => s.DecisionsCompared);
            int ag = r.Segments.Sum(s => s.DecisionsAgreed);
            compared += c;
            agreedDecisions += ag;
            var o = new JsonObject
            {
                ["lineage"] = r.Lineage,
                ["file"] = r.File,
                ["game_version"] = r.GameVersion,
                ["character"] = r.Character,
                ["ascension"] = r.Ascension,
                ["game_mode"] = r.GameMode,
                ["recorded_result"] = r.RecordedResult,
                ["recorded_floor"] = r.RecordedFloor,
                ["segments_in_bundle"] = r.Prepared?.SegmentsInBundle,
                ["dropped_retry_attempts"] = r.Prepared?.DroppedAttempts.Count,
                ["stale_pre_states"] = r.Counters.StalePreStates,
                ["auto_leaves"] = r.Counters.AutoLeaves,
                ["hoisted_decisions"] = r.Counters.Hoisted,
                ["resolved_hand_picks"] = r.Prepared?.ResolvedHandPicks,
                ["reordered_hand_selects"] = r.Prepared?.ReorderedHandSelects,
                ["dropped_card_reward_skips"] = r.Counters.DroppedSkips,
                ["pick_order_fixes"] = r.Counters.PickOrderFixes,
                ["dropped_duplicate_proceeds"] = r.Counters.DroppedDuplicateProceeds,
                ["lagging_relic_lists"] = r.Counters.LaggingRelicLists,
                ["truncated_at"] = r.Prepared?.TruncatedAt,
                ["truncation_reason"] = r.Prepared?.TruncationReason,
                ["error"] = r.Error,
                ["segments_replayed"] = r.Segments.Count(s => s.Status is BundleStatus.Agree or BundleStatus.HardDivergence or BundleStatus.SimException or BundleStatus.Error),
                ["segments_agreed"] = r.Segments.Count(s => s.Status == BundleStatus.Agree),
                ["decisions_compared"] = c,
                ["decisions_agreed"] = ag,
                ["decisions_unobserved"] = r.Segments.Sum(s => s.DecisionsUnobserved),
                ["floor_summaries_agreed"] = r.Segments.Count(s => s.FloorSummaryAgreed),
                ["combat_outcomes_agreed"] = r.Segments.Count(s => s.OutcomeAgreed),
                ["floors_agreed_before_first_divergence"] = floors,
                ["reached_recorded_end"] = ReachedEnd(r),
                ["certified_areas"] = new JsonArray(areas.Select(a => (JsonNode)a).ToArray()),
                ["first_divergence"] = fd != null ? Divergence(r, fd) : null
            };
            runArray.Add(o);
        }
        var areaObj = new JsonObject();
        foreach (var (area, set) in areaRuns)
        {
            areaObj[area] = new JsonArray(set.Select(x => (JsonNode)x).ToArray());
        }
        var classObj = new JsonObject();
        foreach (var (k, v) in byClass)
        {
            classObj[k] = v;
        }
        var floorsList = runs.Select(FloorsAgreed).OrderBy(x => x).ToList();
        return new JsonObject
        {
            ["report"] = "g3-run-replay",
            ["report_version"] = 1,
            ["summary"] = new JsonObject
            {
                ["runs"] = runs.Count,
                ["runs_reaching_recorded_end"] = reached,
                ["decisions_compared"] = compared,
                ["decisions_agreed"] = agreedDecisions,
                ["floors_agreed_sorted"] = new JsonArray(floorsList.Select(x => (JsonNode)x).ToArray()),
                ["first_divergence_by_class"] = classObj
            },
            ["floors_agreed_by_run"] = distribution,
            ["certified_areas"] = areaObj,
            ["runs"] = runArray
        };
    }

    private static JsonObject Divergence(G3RunResult r, SegmentResult fd)
    {
        var seg = r.SegmentById.GetValueOrDefault(fd.SegmentId);
        var d = fd.Divergence;
        int? index = d?.DecisionIndex ?? fd.ExceptionDecisionIndex;
        SegmentDecision? decision = seg != null && index is int i && i >= 0 && i < seg.Decisions.Count ? seg.Decisions[i] : null;
        return new JsonObject
        {
            ["segment"] = fd.SegmentId,
            ["segment_kind"] = fd.SegmentKind,
            ["act"] = seg?.Act,
            ["floor"] = seg?.Floor,
            ["room_type"] = seg?.RoomType,
            ["room_model_id"] = seg?.RoomModelId,
            ["status"] = fd.Status.ToString(),
            ["class"] = fd.Class,
            ["reason"] = fd.Reason,
            ["kind"] = d?.Kind,
            ["decision"] = index,
            ["screen"] = decision?.Screen,
            ["oracle_meta"] = decision?.Oracle?.DeepClone(),
            ["field"] = d?.First.Path,
            ["oracle_value"] = d?.First.Oracle?.DeepClone(),
            ["sim_value"] = d?.First.Sim?.DeepClone(),
            ["note"] = d?.First.Note,
            ["all_hard"] = d != null ? new JsonArray(d.AllHard.Take(12).Select(x => (JsonNode)new JsonObject
            {
                ["path"] = x.Path,
                ["oracle"] = x.Oracle?.DeepClone(),
                ["sim"] = x.Sim?.DeepClone(),
                ["note"] = x.Note
            }).ToArray()) : null,
            ["action"] = d?.Action?.DeepClone() ?? decision?.Action.ToJson(),
            ["exporter_reasons"] = seg != null ? new JsonArray(seg.EligibilityReasons.Select(x => (JsonNode)x).ToArray()) : null,
            ["oracle_projection"] = d?.OracleProjection?.DeepClone(),
            ["sim_projection"] = d?.SimProjection?.DeepClone(),
            ["exception"] = fd.ExceptionDetail is { } ex ? ex[..Math.Min(ex.Length, 1500)] : null
        };
    }

    public static List<string> CertifiedAreas(G3RunResult r)
    {
        var areas = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in r.Segments.Where(x => x.Status == BundleStatus.Agree))
        {
            if (!r.SegmentById.TryGetValue(s.SegmentId, out var seg))
            {
                continue;
            }
            if (seg.FloorSummary != null && s.FloorSummaryAgreed)
            {
                areas.Add("floor_summary");
            }
            if (seg.Kind == SegmentKinds.Combat)
            {
                if (s.OutcomeAgreed)
                {
                    areas.Add("combat");
                }
                continue;
            }
            if (seg.RoomModelId is { } mid)
            {
                string id = RunScreenProjector.StripCategory(mid);
                if (id == "NEOW")
                {
                    areas.Add("neow");
                }
                else if (seg.RoomType == "Event" && seg.Kind == SegmentKinds.Room)
                {
                    areas.Add("event:" + id);
                }
            }
            foreach (int i in s.AgreedIndices)
            {
                var d = seg.Decisions[i];
                areas.Add(d.Screen switch
                {
                    Screens.Map when d.PreState?["map"]?["nodes"] != null => "map_generation",
                    Screens.Map => "map_travel",
                    Screens.Rewards => "rewards",
                    Screens.CardReward => "card_reward",
                    Screens.Shop => "shop",
                    Screens.RestSite => "rest_site",
                    Screens.Treasure => "treasure",
                    Screens.Event => "event_screen",
                    Screens.Select => "deck_select",
                    _ => d.Screen
                });
                if (d.Action.Type == V2Action.NextAct)
                {
                    areas.Add("act_transition");
                }
                if (d.RoomEntered != null)
                {
                    areas.Add("room_roll");
                }
            }
        }
        return areas.ToList();
    }
}
