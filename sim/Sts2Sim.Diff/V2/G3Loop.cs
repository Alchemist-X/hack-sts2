using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.V2;

public static class G3Schema
{
    private static readonly Dictionary<string, string> ScreenAliases = new(StringComparer.Ordinal)
    {
        ["card_select"] = Screens.Select,
        ["bundle_select"] = Screens.Select,
        ["crystal_sphere"] = Screens.Select,
        ["relic_select"] = Screens.Select
    };

    public static readonly IReadOnlySet<string> ActivationDisplayRelics = new HashSet<string>(StringComparer.Ordinal)
    {
        "BOOK_OF_FIVE_RINGS", "FAKE_HAPPY_FLOWER", "HAPPY_FLOWER", "IRON_CLUB", "GALACTIC_DUST", "JOSS_PAPER", "KUNAI", "LASTING_CANDY",
        "KUSARIGAMA", "LETTER_OPENER", "ORNAMENTAL_FAN", "METRONOME", "NUNCHAKU", "PEN_NIB", "PAELS_WING", "SHURIKEN", "PENDULUM",
        "POLLINOUS_CORE", "STONE_CALENDAR", "TOY_BOX", "TUNING_FORK"
    };

    public static bool IsStale(JsonObject pre, string decisionScreen)
    {
        string? screen = pre["screen"]?.GetValue<string>();
        if (screen == null)
        {
            return false;
        }
        if (ScreenAliases.TryGetValue(screen, out var alias))
        {
            screen = alias;
        }
        return !string.Equals(screen, decisionScreen, StringComparison.Ordinal);
    }

    public static JsonObject NormalizeOracle(JsonObject pre, string decisionScreen, out bool stale)
    {
        stale = IsStale(pre, decisionScreen);
        var o = new JsonObject();
        if (pre["player"] is JsonObject player)
        {
            var copy = player.DeepClone().AsObject();
            if (copy["relics"] is JsonArray relics)
            {
                foreach (var r in relics)
                {
                    if (r is JsonObject ro && ro["id"]?.GetValue<string>() is { } rid && ActivationDisplayRelics.Contains(RunScreenProjector.StripCategory(rid)))
                    {
                        ro.Remove("counter");
                    }
                }
            }
            o["player"] = copy;
        }
        if (stale)
        {
            return o;
        }
        switch (decisionScreen)
        {
            case Screens.Event when pre["event"] is JsonObject ev:
                o["event"] = Event(ev);
                break;
            case Screens.Rewards when pre["rewards"] is JsonObject rw:
                o["rewards"] = Rewards(rw);
                break;
            case Screens.CardReward when pre["card_reward"] is JsonObject cr:
                o["card_reward"] = new JsonObject { ["cards"] = CardList(cr["cards"] as JsonArray) };
                break;
            case Screens.Map when pre["map"] is JsonObject map:
                o["map"] = Map(map);
                break;
            case Screens.Shop when pre["shop"] is JsonObject shop:
                o["shop"] = Shop(shop);
                break;
            case Screens.RestSite when pre["rest_site"] is JsonObject rest:
                o["rest_site"] = new JsonObject { ["options"] = new JsonArray(((rest["options"] as JsonArray) ?? new JsonArray()).Select(x => (JsonNode?)new JsonObject { ["id"] = x?["id"]?.DeepClone(), ["enabled"] = x?["enabled"]?.DeepClone() }).ToArray()) };
                break;
            case Screens.Treasure when pre["treasure"] is JsonObject tr:
                var t = new JsonObject();
                if (tr["chest_opened"] is { } opened)
                {
                    t["opened"] = opened.DeepClone();
                }
                if (tr["relics"] is JsonArray relics && relics.Count > 0)
                {
                    t["relics"] = new JsonArray(relics.Select(x => (JsonNode?)JsonValue.Create(x is JsonObject r ? r["id"]?.GetValue<string>() : x?.GetValue<string>())).ToArray());
                }
                o["treasure"] = t;
                break;
            case Screens.Select when pre["select"] is JsonObject sel && sel["candidates"] is JsonArray cands && cands.Count > 0:
                o["select"] = new JsonObject { ["candidates"] = CardList(cands) };
                break;
        }
        return o;
    }

    private static JsonArray CardList(JsonArray? cards)
    {
        var arr = new JsonArray();
        foreach (var c in cards ?? new JsonArray())
        {
            var x = new JsonObject { ["id"] = c?["id"]?.DeepClone() };
            if (c?["upgraded"] is { } up)
            {
                x["upgraded"] = up.DeepClone();
            }
            arr.Add(x);
        }
        return arr;
    }

    private static JsonObject Event(JsonObject ev)
    {
        var o = new JsonObject { ["id"] = ev["event_id"]?.DeepClone() };
        if (ev["options"] is JsonArray options && options.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var opt in options)
            {
                var x = new JsonObject();
                if (opt?["text_key"] is JsonValue key && key.TryGetValue(out string? k) && k != null)
                {
                    x["key"] = k;
                }
                if (opt?["locked"] is { } locked)
                {
                    x["locked"] = locked.DeepClone();
                }
                if (opt?["proceed"] is { } proceed)
                {
                    x["proceed"] = proceed.DeepClone();
                }
                arr.Add(x);
            }
            o["options"] = arr;
        }
        return o;
    }

    private static JsonObject Rewards(JsonObject rw)
    {
        var items = new JsonArray();
        foreach (var it in (rw["items"] as JsonArray) ?? new JsonArray())
        {
            var x = new JsonObject { ["type"] = it?["type"]?.DeepClone() };
            if (it?["gold_amount"] is { } gold)
            {
                x["gold"] = gold.DeepClone();
            }
            if (it?["potion_id"] is { } potion)
            {
                x["potion_id"] = potion.DeepClone();
            }
            if (it?["relic_id"] is { } relic)
            {
                x["relic_id"] = relic.DeepClone();
            }
            if (it?["cards"] is JsonArray cards)
            {
                x["cards"] = CardList(cards);
            }
            items.Add(x);
        }
        return new JsonObject { ["items"] = items };
    }

    private static JsonObject Coord(JsonNode? c, bool withType)
    {
        if (c is JsonArray pair)
        {
            return new JsonObject { ["col"] = pair[0]?.DeepClone(), ["row"] = pair[1]?.DeepClone() };
        }
        var o = new JsonObject { ["col"] = c?["col"]?.DeepClone(), ["row"] = c?["row"]?.DeepClone() };
        if (withType && c?["type"] is JsonValue tv && tv.TryGetValue(out string? t) && t != null)
        {
            o["type"] = RunScreenProjector.Snake(t);
        }
        return o;
    }

    private static int Int(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue(out int i) ? i : -1;

    private static JsonObject Map(JsonObject map)
    {
        var o = new JsonObject();
        if (map["current"] is { } cur)
        {
            o["current"] = Coord(cur, false);
        }
        if (map["next_options"] is JsonArray next)
        {
            o["next_options"] = new JsonArray(next.Select(x => (JsonNode?)Coord(x, true)).ToArray());
        }
        if (map["bosses"] is JsonArray bosses)
        {
            o["boss_ids"] = bosses.DeepClone();
        }
        if (map["nodes"] is JsonArray nodes)
        {
            var list = nodes.Select(n =>
            {
                var x = Coord(n, true);
                var children = ((n?["children"] as JsonArray) ?? new JsonArray()).Select(ch => Coord(ch, false)).OrderBy(ch => Int(ch, "col")).ThenBy(ch => Int(ch, "row"));
                x["children"] = new JsonArray(children.Select(ch => (JsonNode?)ch).ToArray());
                return x;
            }).OrderBy(x => Int(x, "row")).ThenBy(x => Int(x, "col"));
            o["nodes"] = new JsonArray(list.Select(x => (JsonNode?)x).ToArray());
        }
        return o;
    }

    private static JsonObject Shop(JsonObject shop)
    {
        var entries = new JsonArray();
        foreach (var it in (shop["items"] as JsonArray) ?? new JsonArray())
        {
            var x = new JsonObject { ["category"] = it?["category"]?.DeepClone(), ["stocked"] = it?["stocked"]?.DeepClone() };
            if (it?["id"] is JsonValue id && id.TryGetValue(out string? s) && s != null)
            {
                x["id"] = s;
            }
            if (it?["category"]?.GetValue<string>() == "card" && it?["on_sale"] is { } sale)
            {
                x["on_sale"] = sale.DeepClone();
            }
            if (it?["stocked"] is JsonValue st && st.TryGetValue(out bool stocked) && stocked && it?["price"] is { } price)
            {
                x["price"] = price.DeepClone();
            }
            entries.Add(x);
        }
        return new JsonObject { ["entries"] = entries };
    }

    public static JsonObject ProjectSim(RunSim sim)
    {
        var o = RunScreenProjector.Project(sim);
        if (sim.State.Players.Count > 0 && o["player"] is JsonObject player)
        {
            var relics = new JsonArray();
            foreach (var relic in sim.State.Players[0].Relics)
            {
                relics.Add(new JsonObject { ["id"] = relic.Id, ["counter"] = relic.ShowCounter ? relic.DisplayAmount : null });
            }
            player["relics"] = relics;
        }
        if (o["rewards"]?["items"] is JsonArray items)
        {
            var kept = new JsonArray();
            foreach (var it in items)
            {
                if (it?["claimed"] is JsonValue c && c.TryGetValue(out bool claimed) && claimed)
                {
                    continue;
                }
                kept.Add(it!.DeepClone());
            }
            o["rewards"]!["items"] = kept;
        }
        if (o["map"] is JsonObject map && sim.State.Map is { } actMap)
        {
            var points = actMap.GetAllMapPoints().ToList();
            foreach (var extra in new[] { actMap.StartingMapPoint, actMap.BossMapPoint, actMap.SecondBossMapPoint })
            {
                if (extra != null && !points.Contains(extra))
                {
                    points.Add(extra);
                }
            }
            var nodes = new JsonArray();
            foreach (var p in points.OrderBy(p => p.coord.row).ThenBy(p => p.coord.col))
            {
                var n = RunScreenProjector.Coord(p.coord);
                n["type"] = RunScreenProjector.Snake(p.PointType.ToString());
                n["children"] = new JsonArray(p.Children.OrderBy(x => x.coord.col).ThenBy(x => x.coord.row).Select(x => (JsonNode?)RunScreenProjector.Coord(x.coord)).ToArray());
                nodes.Add(n);
            }
            map["nodes"] = nodes;
        }
        if (o["event"] is JsonObject ev && ev["finished"] is JsonValue fv && fv.TryGetValue(out bool finished) && finished)
        {
            ev["options"] = new JsonArray(new JsonObject { ["key"] = "PROCEED", ["locked"] = false, ["proceed"] = true });
        }
        if (o["shop"]?["entries"] is JsonArray entries)
        {
            foreach (var e in entries)
            {
                if (e?["category"]?.GetValue<string>() == "colorless_card")
                {
                    e["category"] = "card";
                }
            }
        }
        return o;
    }
}

public static class G3Loop
{
    private static readonly RunActionKind[] LeaveKinds = { RunActionKind.ProceedRewards, RunActionKind.LeaveShop, RunActionKind.LeaveRestSite, RunActionKind.LeaveEvent, RunActionKind.SkipTreasureRelic };

    public sealed class Counters
    {
        public int StalePreStates;
        public int AutoLeaves;
        public int Hoisted;
        public int DroppedSkips;
        public int PickOrderFixes;
        public int DroppedDuplicateProceeds;
    }

    public static List<SegmentResult> Run(RunBundle run, Counters counters)
    {
        var results = new List<SegmentResult>();
        RunSim? sim = null;
        bool broken = false;
        foreach (var segment in run.Segments)
        {
            var result = new SegmentResult
            {
                Path = run.FilePath,
                Lineage = run.Lineage,
                SegmentId = segment.Id,
                SegmentKind = segment.Kind,
                GameVersion = run.GameVersion,
                Sha256 = segment.Sha256,
                DecisionsTotal = segment.Decisions.Count
            };
            results.Add(result);
            if (broken)
            {
                result.Status = BundleStatus.Skipped;
                result.Class = DivergenceClass.AnchorMissing;
                result.Reason = "segment_after_first_divergence";
                continue;
            }
            try
            {
                if (sim == null)
                {
                    result.StartMode = "seed";
                    sim = AnchorLoader.SeedStart(run, segment.Start ?? new JsonObject { ["from"] = AnchorLoader.FromSeed });
                }
                else
                {
                    result.StartMode = "chained";
                }
                if (segment.Kind == SegmentKinds.Combat)
                {
                    RunCombat(sim, segment, result, counters);
                }
                else
                {
                    RunRoom(sim, segment, result, counters);
                }
            }
            catch (StartFailure ex)
            {
                result.Status = BundleStatus.Error;
                result.Class = ex.Class;
                result.Reason = ex.Message;
            }
            catch (Exception ex)
            {
                Crash(result, ex, -1, "G3 segment setup");
            }
            if (result.Status != BundleStatus.Agree)
            {
                broken = true;
            }
        }
        return results;
    }

    private static void RunCombat(RunSim sim, BundleSegment segment, SegmentResult result, Counters counters)
    {
        if (sim.Phase != RunPhase.Combat)
        {
            var diff = new FieldDiff("sim.phase", "Combat", sim.Phase.ToString(), DiffSeverity.Hard, "the oracle starts a combat here");
            Diverged(result, V2DivergenceKind.RoomEntry, 0, new[] { diff }, null, null, Safe(sim));
            return;
        }
        var roomDiffs = CompareRoom(sim, segment.RoomType, segment.RoomModelId, "segment.room");
        if (roomDiffs.Count > 0)
        {
            Diverged(result, V2DivergenceKind.RoomEntry, 0, roomDiffs, null, null, Safe(sim));
            return;
        }
        V2Runner.ReplayCombat(segment, new RunCombatDriver(sim), result);
    }

    private static void RunRoom(RunSim sim, BundleSegment segment, SegmentResult result, Counters counters)
    {
        if (segment.Kind == SegmentKinds.Room && (segment.RoomType != null || segment.RoomModelId != null))
        {
            var roomDiffs = CompareRoom(sim, segment.RoomType, segment.RoomModelId, "segment.room");
            if (roomDiffs.Count > 0)
            {
                Diverged(result, V2DivergenceKind.RoomEntry, 0, roomDiffs, null, null, Safe(sim));
                return;
            }
        }
        var picked = PickedOffers(segment.FloorSummary);
        int pickPtr = 0;
        var done = new bool[segment.Decisions.Count];
        var disturbed = new bool[segment.Decisions.Count];
        for (int i = 0; i < segment.Decisions.Count; i++)
        {
            if (done[i])
            {
                continue;
            }
            var d = segment.Decisions[i];
            var touched = new HashSet<ContentKey>();
            ContentIndex.Collect(d.PreState, null, touched);
            ContentIndex.Collect(d.Action.Raw, null, touched);
            result.Touched[i] = touched;
            if (d.Action.Type is V2Action.MapMove or V2Action.NextAct && sim.Phase != RunPhase.Map && !sim.HasPendingChoice)
            {
                AutoLeave(sim, d.Action.Type == V2Action.NextAct, counters);
            }
            for (int guard = 0; guard < 4 && d.Action.Type == V2Action.EventProceed && sim.Phase == RunPhase.Rewards && !sim.HasPendingChoice && sim.IsLegal(RunAction.Of(RunActionKind.ProceedRewards)); guard++)
            {
                sim.Step(RunAction.Of(RunActionKind.ProceedRewards));
                counters.AutoLeaves++;
            }
            if (sim.IsTerminal)
            {
                var diff = new FieldDiff("sim.phase", "in_progress", RunScreenProjector.ScreenOf(sim), DiffSeverity.Hard, "the oracle still has a decision here but the sim run is over");
                Diverged(result, V2DivergenceKind.Terminal, i, new[] { diff }, d, d.PreState, Safe(sim));
                return;
            }
            if (d.Action.Type == V2Action.EventProceed && sim.Phase == RunPhase.Map && d.PreState?["screen"]?.GetValue<string>() == Screens.Map)
            {
                counters.DroppedDuplicateProceeds++;
                done[i] = true;
                continue;
            }
            if (d.Action.Type == V2Action.CardRewardSkip && RunLegalActionGenerator.OpenCardReward(sim.State, sim.Loop) == null && sim.Phase == RunPhase.Rewards)
            {
                counters.DroppedSkips++;
                done[i] = true;
                continue;
            }
            JsonObject simProjection;
            try
            {
                simProjection = G3Schema.ProjectSim(sim);
            }
            catch (Exception ex)
            {
                Crash(result, ex, i, "RunScreenProjector.Project");
                return;
            }
            string simScreen = RunScreenProjector.ScreenOf(sim);
            bool screenOk = simScreen == d.Screen
                || (simScreen == Screens.Select && d.Screen == Screens.Combat && d.Action.Type == V2Action.Choose)
                || (d.Action.Type == V2Action.NextAct && simScreen is Screens.Rewards or Screens.Map);
            if (!screenOk)
            {
                if (TryHoist(sim, segment, i, done, counters))
                {
                    disturbed[i] = true;
                    i--;
                    continue;
                }
                var diff = new FieldDiff("screen", d.Screen, simScreen, DiffSeverity.Hard);
                Diverged(result, V2DivergenceKind.Screen, i, new[] { diff }, d, d.PreState, simProjection);
                return;
            }
            bool compare = d.PreState is JsonObject preObj && !disturbed[i] && !G3Schema.IsStale(preObj, d.Screen);
            if (d.PreState is JsonObject stalePre && G3Schema.IsStale(stalePre, d.Screen))
            {
                counters.StalePreStates++;
            }
            if (compare)
            {
                var oracle = G3Schema.NormalizeOracle((JsonObject)d.PreState!, d.Screen, out bool stale);
                if (stale)
                {
                    counters.StalePreStates++;
                }
                var diffs = RunScreenComparer.Compare(oracle, simProjection);
                var hard = diffs.Where(x => x.Severity == DiffSeverity.Hard).ToList();
                if (hard.Count > 0 && Probe(sim, d.Action) != null && TryHoist(sim, segment, i, done, counters))
                {
                    disturbed[i] = true;
                    i--;
                    continue;
                }
                result.DecisionsCompared++;
                foreach (var soft in diffs.Where(x => x.Severity == DiffSeverity.Soft))
                {
                    result.SoftDiffs.Add(new SoftDiff(i, soft));
                }
                if (hard.Count > 0)
                {
                    Diverged(result, V2DivergenceKind.State, i, hard, d, oracle, simProjection);
                    return;
                }
            }
            else
            {
                result.DecisionsUnobserved++;
            }
            if (d.Action.Type is V2Action.CardRewardPick or V2Action.CardRewardSkip && d.Oracle?["offered"] is JsonArray offered)
            {
                var reward = RunLegalActionGenerator.OpenCardReward(sim.State, sim.Loop);
                var simOffer = new JsonArray((reward?.Cards ?? Array.Empty<CardCreationResult>()).Select(c => (JsonNode?)new JsonObject { ["id"] = c.Card.Id }).ToArray());
                var oracleOffer = new JsonArray(offered.Select(x => (JsonNode?)new JsonObject { ["id"] = x?.DeepClone() }).ToArray());
                var offerDiffs = new List<FieldDiff>();
                RunScreenComparer.CompareMultiset("card_reward.offered", oracleOffer, simOffer, offerDiffs);
                bool subset = reward != null && reward.Cards.All(c => offered.Any(x => RunScreenComparer.ScalarEquals(x, JsonValue.Create(c.Card.Id))));
                if (offerDiffs.Any(x => x.Severity == DiffSeverity.Hard) && !(subset && offered.Count > reward!.Cards.Count))
                {
                    Diverged(result, V2DivergenceKind.State, i, offerDiffs.Where(x => x.Severity == DiffSeverity.Hard).ToList(), d, oracleOffer, simOffer);
                    return;
                }
            }
            FieldDiff? actionDiff;
            try
            {
                actionDiff = TryPickedOrder(sim, d.Action, picked, ref pickPtr, counters) ? null
                    : TryEventBySuffix(sim, d.Action) ? null : ActionMapper.ApplyRun(sim, d.Action);
                if (actionDiff == null && d.Action.Type == V2Action.CardRewardPick && pickPtr < picked.Count && sim.State.Players[0].Deck.Count > 0
                    && sim.State.Players[0].Deck[^1].Id == picked[pickPtr])
                {
                    pickPtr++;
                }
            }
            catch (Exception ex)
            {
                Crash(result, ex, i, $"RunSim.Step({d.Action.Type})");
                return;
            }
            if (actionDiff != null)
            {
                if (TryHoist(sim, segment, i, done, counters))
                {
                    disturbed[i] = true;
                    if (compare)
                    {
                        result.DecisionsCompared--;
                    }
                    i--;
                    continue;
                }
                Diverged(result, V2DivergenceKind.Action, i, new[] { actionDiff }, d, d.PreState, simProjection);
                return;
            }
            done[i] = true;
            result.ActionsPrefix.Add(d.Action.ToJson());
            if (d.RoomEntered != null)
            {
                var roomDiffs = CompareRoom(sim, d.RoomEntered["room_type"]?.GetValue<string>(), d.RoomEntered["model_id"]?.GetValue<string>(), "room_entered");
                if (roomDiffs.Count > 0)
                {
                    Diverged(result, V2DivergenceKind.RoomEntry, i, roomDiffs, d, d.RoomEntered, Safe(sim));
                    return;
                }
            }
            if (compare)
            {
                result.DecisionsAgreed++;
                result.AgreedIndices.Add(i);
            }
        }
        if (segment.FloorSummary != null)
        {
            result.FloorSummaryChecked = true;
            var entry = V2Runner.FloorEntry(sim.State, segment.Floor);
            if (entry == null)
            {
                var diff = new FieldDiff("floor_summary", segment.FloorSummary.DeepClone(), null, DiffSeverity.Hard, $"the sim has no map point history entry for floor {segment.Floor}");
                Diverged(result, V2DivergenceKind.FloorSummary, segment.Decisions.Count, new[] { diff }, null, segment.FloorSummary, null);
                return;
            }
            var simSummary = RunScreenProjector.FloorSummary(entry);
            var diffs = RunScreenComparer.CompareFloorSummary(segment.FloorSummary, simSummary);
            var hard = diffs.Where(x => x.Severity == DiffSeverity.Hard).ToList();
            if (hard.Count > 0)
            {
                Diverged(result, V2DivergenceKind.FloorSummary, segment.Decisions.Count, hard, null, segment.FloorSummary, simSummary);
                return;
            }
            result.FloorSummaryAgreed = true;
        }
        result.Status = BundleStatus.Agree;
    }

    private const int HoistWindow = 8;

    private static List<string> PickedOffers(JsonObject? summary)
    {
        var list = new List<string>();
        if (summary?["player_stats"]?["card_choices"] is JsonArray choices)
        {
            foreach (var c in choices)
            {
                if (c?["was_picked"] is JsonValue p && p.TryGetValue(out bool wp) && wp && c["card"]?["id"]?.GetValue<string>() is { } id)
                {
                    list.Add(RunScreenProjector.StripCategory(id));
                }
            }
        }
        return list;
    }

    private static bool TryPickedOrder(RunSim sim, V2Action a, List<string> picked, ref int ptr, Counters counters)
    {
        if (ptr >= picked.Count)
        {
            return false;
        }
        string expected = picked[ptr];
        if (a.Type == V2Action.RewardTake && a.Str("reward_type") == "card" && a.Str("item_id") == null && RunLegalActionGenerator.TopRewards(sim.State) is { } set)
        {
            int first = -1;
            int match = -1;
            for (int i = 0; i < set.Rewards.Count; i++)
            {
                if (set.Rewards[i] is CardReward cr && !cr.SuccessfullySelected)
                {
                    if (first < 0) first = i;
                    if (match < 0 && cr.IsPopulated && cr.Cards.Any(c => c.Card.Id == expected)) match = i;
                }
            }
            if (match < 0 || match == first)
            {
                return false;
            }
            var claim = RunAction.Of(RunActionKind.ClaimReward, match);
            if (!sim.IsLegal(claim))
            {
                return false;
            }
            sim.Step(claim);
            counters.PickOrderFixes++;
            return true;
        }
        if (a.Type == V2Action.CardRewardPick && a.Str("card_id") == null && RunLegalActionGenerator.OpenCardReward(sim.State, sim.Loop) is { } open)
        {
            int index = open.Cards.ToList().FindIndex(c => c.Card.Id == expected);
            if (index < 0 || index == (a.Int("index") ?? -1))
            {
                return false;
            }
            var pick = RunAction.Of(RunActionKind.PickRewardCard, index);
            if (!sim.IsLegal(pick))
            {
                return false;
            }
            sim.Step(pick);
            ptr++;
            counters.PickOrderFixes++;
            return true;
        }
        return false;
    }

    private static FieldDiff? Probe(RunSim sim, V2Action a)
    {
        var clone = sim.Clone();
        try
        {
            return TryEventBySuffix(clone, a) ? null : ActionMapper.ApplyRun(clone, a);
        }
        catch (Exception ex)
        {
            return new FieldDiff("action", a.Type, ex.GetType().Name, DiffSeverity.Hard, ex.Message);
        }
    }

    private static bool TryHoist(RunSim sim, BundleSegment segment, int i, bool[] done, Counters counters)
    {
        for (int j = i + 1; j < segment.Decisions.Count && j <= i + HoistWindow; j++)
        {
            if (done[j])
            {
                continue;
            }
            var a = segment.Decisions[j].Action;
            if (a.Type is V2Action.MapMove or V2Action.NextAct or V2Action.EventProceed or V2Action.CardRewardSkip)
            {
                continue;
            }
            if (Probe(sim, a) != null)
            {
                continue;
            }
            if ((TryEventBySuffix(sim, a) ? null : ActionMapper.ApplyRun(sim, a)) != null)
            {
                continue;
            }
            done[j] = true;
            counters.Hoisted++;
            return true;
        }
        return false;
    }

    public static bool TryEventBySuffix(RunSim sim, V2Action a)
    {
        if (a.Type is not (V2Action.EventOption or V2Action.EventProceed) || a.Str("text_key") is not { } key)
        {
            return false;
        }
        var ev = RunLegalActionGenerator.CurrentEvent(sim.State);
        if (ev == null || ev.IsFinished)
        {
            return false;
        }
        var options = ev.CurrentOptions;
        if (options.Any(o => string.Equals(o.TextKey, key, StringComparison.Ordinal)))
        {
            return false;
        }
        int index = -1;
        for (int i = 0; i < options.Count && index < 0; i++)
        {
            if (options[i].TextKey.EndsWith("." + key, StringComparison.Ordinal) || string.Equals(options[i].RelicId, key, StringComparison.Ordinal))
            {
                index = i;
            }
        }
        if (index < 0)
        {
            return false;
        }
        var action = RunAction.Of(RunActionKind.ChooseEventOption, index);
        if (!sim.IsLegal(action))
        {
            return false;
        }
        sim.Step(action);
        return true;
    }

    private static void AutoLeave(RunSim sim, bool nextAct, Counters counters)
    {
        for (int guard = 0; guard < 4 && sim.Phase != RunPhase.Map && !sim.IsTerminal && !sim.HasPendingChoice; guard++)
        {
            if (nextAct && sim.Phase == RunPhase.Rewards)
            {
                return;
            }
            var legal = sim.LegalActions();
            int k = -1;
            for (int j = 0; j < legal.Count && k < 0; j++)
            {
                if (Array.IndexOf(LeaveKinds, legal[j].Kind) >= 0)
                {
                    k = j;
                }
            }
            if (k < 0)
            {
                return;
            }
            sim.Step(legal[k]);
            counters.AutoLeaves++;
        }
    }

    private static List<FieldDiff> CompareRoom(RunSim sim, string? roomType, string? modelId, string prefix)
    {
        var diffs = new List<FieldDiff>();
        var room = RoomStack.CurrentRoom(sim.State);
        string? simType = room != null ? RunScreenProjector.Snake(room.RoomType.ToString()) : null;
        if (roomType != null && !string.Equals(RunScreenProjector.Snake(roomType), simType, StringComparison.Ordinal))
        {
            diffs.Add(new FieldDiff(prefix + ".room_type", roomType, simType, DiffSeverity.Hard));
        }
        if (modelId != null && !string.Equals(RunScreenProjector.StripCategory(modelId), room?.ModelId, StringComparison.Ordinal))
        {
            diffs.Add(new FieldDiff(prefix + ".model_id", modelId, room?.ModelId, DiffSeverity.Hard));
        }
        return diffs;
    }

    private static void Diverged(SegmentResult result, string kind, int index, IReadOnlyList<FieldDiff> all, SegmentDecision? d, JsonNode? oracleProjection, JsonNode? simProjection)
    {
        result.Status = BundleStatus.HardDivergence;
        result.Divergence = new SegmentDivergence
        {
            Kind = kind,
            DecisionIndex = index,
            First = all[0],
            AllHard = all,
            Action = d?.Action.ToJson(),
            OracleProjection = oracleProjection?.DeepClone(),
            SimProjection = simProjection
        };
        result.Reason = $"{kind} @ {index}: {all[0].Path}";
        result.Class = Triage.ClassifyDivergence(result);
    }

    private static void Crash(SegmentResult result, Exception ex, int index, string where)
    {
        result.Status = BundleStatus.SimException;
        result.ExceptionDecisionIndex = index;
        result.Reason = $"{where} threw {ex.GetType().Name}: {ex.Message}";
        result.ExceptionDetail = ex.ToString();
        result.Class = Triage.ClassifyException(ex);
    }

    private static JsonNode? Safe(RunSim sim)
    {
        try
        {
            return G3Schema.ProjectSim(sim);
        }
        catch (Exception ex)
        {
            return JsonValue.Create($"<projection failed: {ex.GetType().Name}: {ex.Message}>");
        }
    }
}
