using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.V2;

public sealed record V2Options
{
    public bool IncludeIneligible { get; init; }
}

public static class V2Runner
{
    public static List<SegmentResult> RunBundle(RunBundle run, V2Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        options ??= new V2Options();
        var results = new List<SegmentResult>(run.Segments.Count);
        RunSim? live = null;
        bool chainBroken = false;
        foreach (var segment in run.Segments)
        {
            var result = new SegmentResult
            {
                Path = run.FilePath,
                Lineage = run.Lineage,
                SegmentId = segment.Id,
                SegmentKind = segment.Kind,
                GameVersion = run.GameVersion,
                FromV1 = run.FromV1,
                Sha256 = segment.Sha256,
                DecisionsTotal = segment.Decisions.Count
            };
            CollectSegmentTouches(segment, result);
            results.Add(result);
            string? runGate = RunGate(run);
            if (runGate != null)
            {
                Skip(result, DivergenceClass.OutOfScope, runGate);
                live = null;
                continue;
            }
            RunSim? next;
            try
            {
                next = segment.Kind == SegmentKinds.Combat
                    ? RunCombatSegment(run, segment, options, live, chainBroken, result)
                    : RunRunSegment(run, segment, options, live, chainBroken, result);
            }
            catch (StartFailure ex)
            {
                if (ex.IsError)
                {
                    result.Status = BundleStatus.Error;
                    result.Reason = ex.Message;
                    result.Class = ex.Class;
                }
                else
                {
                    Skip(result, ex.Class, ex.Message);
                }
                next = null;
            }
            chainBroken = result.Status is not (BundleStatus.Agree or BundleStatus.Skipped) || (chainBroken && next == null);
            live = result.Status == BundleStatus.Agree ? next : null;
        }
        return results;
    }

    public static string? RunGate(RunBundle run)
    {
        if (!string.Equals(run.GameMode, "standard", StringComparison.Ordinal))
        {
            return $"game_mode {run.GameMode} is out of scope";
        }
        if (run.Modifiers.Count > 0)
        {
            return $"modifiers [{string.Join(", ", run.Modifiers)}] are out of scope";
        }
        if (run.RulesVersion != null && !string.Equals(run.RulesVersion, run.GameVersion, StringComparison.Ordinal) && !run.FromV1)
        {
            return $"rules_version {run.RulesVersion} differs from game_version {run.GameVersion}";
        }
        return null;
    }

    private static void Skip(SegmentResult result, string cls, string reason)
    {
        result.Status = BundleStatus.Skipped;
        result.Class = cls;
        result.Reason = reason;
    }

    public static string ClassifySkip(string reason)
    {
        if (reason.StartsWith("character ", StringComparison.Ordinal))
        {
            return DivergenceClass.ContentNotPorted;
        }
        if (reason.Contains("not ported", StringComparison.Ordinal) || reason.Contains("whitelist", StringComparison.Ordinal))
        {
            return DivergenceClass.ContentNotPorted;
        }
        if (reason.Contains("'old' prefix", StringComparison.Ordinal) || reason.Contains("prefix", StringComparison.Ordinal))
        {
            return DivergenceClass.VersionDrift;
        }
        if (reason.Contains("exporter marked", StringComparison.Ordinal))
        {
            return DivergenceClass.ExporterIneligible;
        }
        return DivergenceClass.OutOfScope;
    }

    private static RunSim? RunCombatSegment(RunBundle run, BundleSegment segment, V2Options options, RunSim? live, bool chainBroken, SegmentResult result)
    {
        CombatDriver driver;
        if (segment.CombatStart is { } shell)
        {
            result.StartMode = run.FromV1 ? "v1" : "v1_start";
            string? skip = BundleRunner.IneligibleReason(shell, options.IncludeIneligible);
            if (skip != null)
            {
                Skip(result, ClassifySkip(skip), skip);
                return null;
            }
            result.ReplayedIneligible = shell.IneligibleReasons.Count > 0;
            foreach (var potion in RunBundleJson.StartPotions(segment))
            {
                if (!PotionRegistry.Models.Contains(RunScreenProjector.StripCategory(potion.Id)))
                {
                    Skip(result, DivergenceClass.ContentNotPorted, $"potion {potion.Id} is not ported");
                    return null;
                }
            }
            string? inconsistency = CombatConsistency(segment);
            if (inconsistency != null)
            {
                result.Status = BundleStatus.Error;
                result.Class = DivergenceClass.ExporterBug;
                result.Reason = "bundle inconsistent: " + inconsistency;
                return null;
            }
            CombatSim sim;
            try
            {
                sim = BundleRunner.CreateSim(shell);
                foreach (var potion in RunBundleJson.StartPotions(segment))
                {
                    sim.State.Player.AddPotionInternal(PotionRegistry.Create(RunScreenProjector.StripCategory(potion.Id)), potion.Slot);
                }
            }
            catch (ArgumentException ex)
            {
                result.Status = BundleStatus.Error;
                result.Class = DivergenceClass.ExporterBug;
                result.Reason = "sim setup rejected the bundle inputs: " + ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                Crash(result, ex, -1, "CombatSim.Create");
                return null;
            }
            driver = new StandaloneCombatDriver(sim);
        }
        else
        {
            var runSim = StartRunSim(run, segment, live, chainBroken, result);
            if (runSim.Phase != RunPhase.Combat)
            {
                throw new StartFailure(DivergenceClass.AnchorError, $"the segment start is in phase {runSim.Phase}, not in a combat");
            }
            driver = new RunCombatDriver(runSim);
        }
        ReplayCombat(segment, driver, result);
        return result.Status == BundleStatus.Agree ? driver.Run : null;
    }

    private static string? CombatConsistency(BundleSegment segment)
    {
        foreach (var d in segment.Decisions)
        {
            if (d.Action.Type != V2Action.PlayCard || !RunScreenComparer.IsCombatProjection(d.PreState))
            {
                continue;
            }
            var hand = d.PreState!["player"]?["hand"] as JsonArray;
            int handIndex = d.Action.Int("hand_index") ?? -1;
            if (hand == null || handIndex >= hand.Count)
            {
                return $"decision {d.Index}: hand_index {handIndex} is outside pre_state hand (size {hand?.Count ?? 0})";
            }
            var card = hand[handIndex] as JsonObject;
            string? id = card?["id"]?.GetValue<string>();
            if (d.Action.Str("card_id") is { } cid && !string.Equals(cid, id, StringComparison.Ordinal))
            {
                return $"decision {d.Index}: action card_id {cid} but pre_state hand[{handIndex}] is {id}";
            }
            if (d.Action.Bool("upgraded") is bool u && card?["upgraded"] is JsonValue uv && uv.TryGetValue(out bool cu) && u != cu)
            {
                return $"decision {d.Index}: action upgraded={u} but pre_state hand[{handIndex}] upgraded={cu}";
            }
        }
        return null;
    }

    private static RunSim StartRunSim(RunBundle run, BundleSegment segment, RunSim? live, bool chainBroken, SegmentResult result)
    {
        string? from = AnchorLoader.StartFrom(segment);
        if (from == AnchorLoader.FromSeed)
        {
            result.StartMode = "seed";
            return AnchorLoader.SeedStart(run, segment.Start!);
        }
        var anchor = run.FindAnchor(segment.StartAnchor);
        if (anchor != null && anchor.Kind != AnchorLoader.AnchorMcrInitial)
        {
            result.StartMode = "anchor:" + anchor.Kind;
            return AnchorLoader.LoadSaveAnchor(run, anchor);
        }
        if (anchor != null)
        {
            throw new StartFailure(DivergenceClass.AnchorMissing, $"anchor_missing: {anchor.Id} is an mcr_initial anchor without a v1-shaped start");
        }
        if (live != null)
        {
            result.StartMode = "chained";
            return live;
        }
        if (chainBroken)
        {
            throw new StartFailure(DivergenceClass.AnchorMissing, "segment_after_unclassified_divergence: no anchor and the previous segment did not agree");
        }
        throw new StartFailure(DivergenceClass.AnchorMissing, "anchor_missing: the segment has no start, no anchor and no agreeing predecessor");
    }

    private static RunSim? RunRunSegment(RunBundle run, BundleSegment segment, V2Options options, RunSim? live, bool chainBroken, SegmentResult result)
    {
        if (segment.Eligible == false && !options.IncludeIneligible)
        {
            Skip(result, DivergenceClass.ExporterIneligible, "the exporter marked this segment ineligible (" + string.Join("; ", segment.EligibilityReasons) + ")");
            return null;
        }
        result.ReplayedIneligible = segment.Eligible == false;
        var sim = StartRunSim(run, segment, live, chainBroken, result);
        if (segment.RoomType != null || segment.RoomModelId != null)
        {
            var roomDiffs = CompareRoom(sim, segment.RoomType, segment.RoomModelId, "segment.room");
            if (roomDiffs.Count > 0)
            {
                Diverged(result, V2DivergenceKind.RoomEntry, 0, roomDiffs, null, null, SafeRunProjection(sim));
                return null;
            }
        }
        for (int i = 0; i < segment.Decisions.Count; i++)
        {
            var d = segment.Decisions[i];
            NoteTouches(result, d);
            if (sim.IsTerminal)
            {
                var diff = new FieldDiff("sim.phase", "in_progress", RunScreenProjector.ScreenOf(sim), DiffSeverity.Hard, "the oracle still has a decision here but the sim run is over");
                Diverged(result, V2DivergenceKind.Terminal, i, new[] { diff }, d, d.PreState, SafeRunProjection(sim));
                return null;
            }
            JsonObject simProjection;
            try
            {
                simProjection = RunScreenProjector.Project(sim);
            }
            catch (ProjectionGapException ex)
            {
                result.Status = BundleStatus.Error;
                result.Class = DivergenceClass.ProjectionGap;
                result.Reason = $"decision {i}: projection gap: {ex.Message}";
                return null;
            }
            catch (Exception ex)
            {
                Crash(result, ex, i, "RunScreenProjector.Project");
                return null;
            }
            string simScreen = RunScreenProjector.ScreenOf(sim);
            bool screenOk = simScreen == d.Screen || (simScreen == Screens.Select && d.Screen == Screens.Combat && d.Action.Type == V2Action.Choose);
            if (!screenOk)
            {
                var diff = new FieldDiff("screen", d.Screen, simScreen, DiffSeverity.Hard);
                Diverged(result, V2DivergenceKind.Screen, i, new[] { diff }, d, d.PreState, simProjection);
                return null;
            }
            if (d.PreState != null)
            {
                List<FieldDiff> diffs;
                try
                {
                    var ignored = new SortedSet<string>(StringComparer.Ordinal);
                    diffs = RunScreenComparer.IsCombatProjection(d.PreState) && simProjection["combat"] is JsonObject simCombat
                        ? CombatDiffs(d.PreState, simCombat, ignored)
                        : RunScreenComparer.Compare(d.PreState, simProjection, ignored);
                }
                catch (BundleFormatException ex)
                {
                    result.Status = BundleStatus.Error;
                    result.Class = DivergenceClass.ExporterBug;
                    result.Reason = $"decision {i}: pre_state: {ex.Message}";
                    return null;
                }
                result.DecisionsCompared++;
                if (Record(result, i, diffs, d, simProjection))
                {
                    return null;
                }
            }
            else
            {
                result.DecisionsUnobserved++;
            }
            FieldDiff? actionDiff;
            try
            {
                actionDiff = ActionMapper.ApplyRun(sim, d.Action);
            }
            catch (Exception ex)
            {
                Crash(result, ex, i, $"RunSim.Step({d.Action.Type})");
                return null;
            }
            if (actionDiff != null)
            {
                Diverged(result, V2DivergenceKind.Action, i, new[] { actionDiff }, d, d.PreState, simProjection);
                return null;
            }
            result.ActionsPrefix.Add(d.Action.ToJson());
            if (d.RoomEntered != null)
            {
                var roomDiffs = CompareRoom(sim, d.RoomEntered["room_type"]?.GetValue<string>(), d.RoomEntered["model_id"]?.GetValue<string>(), "room_entered");
                if (roomDiffs.Count > 0)
                {
                    Diverged(result, V2DivergenceKind.RoomEntry, i, roomDiffs, d, d.RoomEntered, SafeRunProjection(sim));
                    return null;
                }
            }
            if (d.PreState != null)
            {
                result.DecisionsAgreed++;
                result.AgreedIndices.Add(i);
            }
        }
        if (segment.FloorSummary != null)
        {
            result.FloorSummaryChecked = true;
            var entry = FloorEntry(sim.State, segment.Floor);
            if (entry == null)
            {
                var diff = new FieldDiff("floor_summary", segment.FloorSummary.DeepClone(), null, DiffSeverity.Hard, $"the sim has no map point history entry for floor {segment.Floor?.ToString() ?? "(last)"}");
                Diverged(result, V2DivergenceKind.FloorSummary, segment.Decisions.Count, new[] { diff }, null, segment.FloorSummary, null);
                return null;
            }
            var simSummary = RunScreenProjector.FloorSummary(entry);
            var diffs = RunScreenComparer.CompareFloorSummary(segment.FloorSummary, simSummary);
            foreach (var soft in diffs.Where(x => x.Severity == DiffSeverity.Soft))
            {
                result.SoftDiffs.Add(new SoftDiff(segment.Decisions.Count, soft));
            }
            var hard = diffs.Where(x => x.Severity == DiffSeverity.Hard).ToList();
            if (hard.Count > 0)
            {
                Diverged(result, V2DivergenceKind.FloorSummary, segment.Decisions.Count, hard, null, segment.FloorSummary, simSummary);
                return null;
            }
            result.FloorSummaryAgreed = true;
        }
        result.Status = BundleStatus.Agree;
        return sim;
    }

    public static MapPointHistoryEntry? FloorEntry(RunState rs, int? floor)
    {
        var history = rs.MapPointHistory;
        if (floor is int f)
        {
            return f >= 1 && f <= history.Count ? history[f - 1] : null;
        }
        return history.Count == 0 ? null : history[^1];
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

    public static void ReplayCombat(BundleSegment segment, CombatDriver driver, SegmentResult result)
    {
        for (int i = 0; i < segment.Decisions.Count; i++)
        {
            var d = segment.Decisions[i];
            NoteTouches(result, d);
            if (driver.IsTerminal)
            {
                var diff = new FieldDiff("sim.outcome", "in_progress", driver.OutcomeName, DiffSeverity.Hard, "the oracle still has a decision here but the sim combat is over");
                Diverged(result, V2DivergenceKind.Terminal, i, new[] { diff }, d, d.PreState, SafeCombatProjection(driver));
                return;
            }
            JsonObject? simJson = null;
            if (d.PreState != null)
            {
                List<FieldDiff> diffs;
                try
                {
                    diffs = CombatDecisionDiffs(d, driver, result, out simJson);
                }
                catch (ProjectionGapException ex)
                {
                    result.Status = BundleStatus.Error;
                    result.Class = DivergenceClass.ProjectionGap;
                    result.Reason = $"decision {i}: projection gap: {ex.Message}";
                    return;
                }
                catch (BundleFormatException ex)
                {
                    result.Status = BundleStatus.Error;
                    result.Class = DivergenceClass.ExporterBug;
                    result.Reason = $"decision {i}: pre_state: {ex.Message}";
                    return;
                }
                catch (Exception ex)
                {
                    Crash(result, ex, i, "SimProjector.Project");
                    return;
                }
                result.DecisionsCompared++;
                if (Record(result, i, diffs, d, simJson))
                {
                    return;
                }
            }
            else
            {
                if (driver.State != null && driver.PendingChoice == null)
                {
                    try
                    {
                        SimProjector.Project(driver.State);
                    }
                    catch (ProjectionGapException ex)
                    {
                        result.Status = BundleStatus.Error;
                        result.Class = DivergenceClass.ProjectionGap;
                        result.Reason = $"decision {i}: projection gap: {ex.Message}";
                        return;
                    }
                    catch (Exception ex)
                    {
                        Crash(result, ex, i, "SimProjector.Project");
                        return;
                    }
                }
                result.DecisionsUnobserved++;
            }
            FieldDiff? actionDiff;
            try
            {
                actionDiff = ActionMapper.ApplyCombat(driver, d.Action);
            }
            catch (Exception ex)
            {
                Crash(result, ex, i, $"Step({d.Action.Type})");
                return;
            }
            if (actionDiff != null)
            {
                Diverged(result, V2DivergenceKind.Action, i, new[] { actionDiff }, d, d.PreState, simJson ?? SafeCombatProjection(driver));
                return;
            }
            result.ActionsPrefix.Add(d.Action.ToJson());
            if (d.PreState != null)
            {
                result.DecisionsAgreed++;
                result.AgreedIndices.Add(i);
            }
        }

        result.SimOutcome = driver.OutcomeName;
        result.SimFinalHp = driver.PlayerHp;
        if (segment.OutcomeResult != null || segment.OutcomeFinalHp != null)
        {
            result.OutcomeChecked = true;
            var finalDiffs = new List<FieldDiff>();
            if (segment.OutcomeResult != null && !string.Equals(segment.OutcomeResult, result.SimOutcome, StringComparison.Ordinal))
            {
                finalDiffs.Add(new FieldDiff("outcome", segment.OutcomeResult, result.SimOutcome, DiffSeverity.Hard));
            }
            if (segment.OutcomeFinalHp is int hp && hp != result.SimFinalHp)
            {
                finalDiffs.Add(new FieldDiff("final_hp", hp, result.SimFinalHp, DiffSeverity.Hard));
            }
            result.OutcomeAgreed = finalDiffs.Count == 0;
            if (finalDiffs.Count > 0)
            {
                Diverged(result, V2DivergenceKind.Final, segment.Decisions.Count, finalDiffs, null, null, SafeCombatProjection(driver));
                return;
            }
        }
        result.Status = BundleStatus.Agree;
    }

    private static List<FieldDiff> CombatDecisionDiffs(SegmentDecision d, CombatDriver driver, SegmentResult result, out JsonObject? simJson)
    {
        var pre = d.PreState!;
        var diffs = new List<FieldDiff>();
        simJson = null;
        if (pre is JsonObject preObj && preObj["select"] is JsonObject oracleSelect)
        {
            var simSelect = RunScreenProjector.Select(driver.PendingChoice, driver.PendingPicks);
            simJson = new JsonObject { ["select"] = simSelect };
            if (driver.PendingChoice == null)
            {
                diffs.Add(new FieldDiff("select", oracleSelect.DeepClone(), null, DiffSeverity.Hard, "the oracle shows a selection screen but the sim has no open prompt"));
            }
            else
            {
                diffs.AddRange(RunScreenComparer.Compare(new JsonObject { ["select"] = oracleSelect.DeepClone() }, simJson));
            }
            if (!RunScreenComparer.IsCombatProjection(pre))
            {
                return diffs;
            }
        }
        if (RunScreenComparer.IsCombatProjection(pre))
        {
            var cs = driver.State ?? throw new InvalidOperationException("the sim has no combat state to project");
            var projection = SimProjector.Project(cs);
            var simCombat = ProjectionJson.Write(projection);
            var ignored = new SortedSet<string>(StringComparer.Ordinal);
            var oracle = ProjectionJson.Read(pre, "pre_state", ignored);
            diffs.AddRange(ProjectionComparer.Compare(oracle, projection));
            if (pre["player"]?["potions"] is JsonArray oraclePotions)
            {
                var simPotions = RunScreenProjector.Player(cs.Player)["potions"]!;
                RunScreenComparer.CompareNode("player.potions", oraclePotions, simPotions, diffs);
                simCombat["player"]!["potions"] = simPotions.DeepClone();
            }
            if (simJson != null)
            {
                simJson["combat"] = simCombat;
            }
            else
            {
                simJson = simCombat;
            }
            return diffs.Where(x => x.Severity == DiffSeverity.Hard).Concat(diffs.Where(x => x.Severity == DiffSeverity.Soft)).ToList();
        }
        if (diffs.Count == 0 && simJson == null)
        {
            diffs.Add(new FieldDiff("pre_state", pre.DeepClone(), null, DiffSeverity.Hard, "a combat pre_state is a v1 projection or a 'select' projection"));
        }
        return diffs;
    }

    private static List<FieldDiff> CombatDiffs(JsonNode oracle, JsonObject simCombat, ISet<string> ignored)
    {
        var diffs = new List<FieldDiff>();
        RunScreenComparer.CompareCombat(oracle, simCombat, "combat", diffs, ignored);
        return diffs.Select(x => x with { Path = x.Path["combat.".Length..] }).ToList();
    }

    private static bool Record(SegmentResult result, int i, List<FieldDiff> diffs, SegmentDecision d, JsonNode? simProjection)
    {
        foreach (var diff in diffs)
        {
            if (diff.Severity == DiffSeverity.Soft)
            {
                result.SoftDiffs.Add(new SoftDiff(i, diff));
            }
        }
        var hard = diffs.Where(x => x.Severity == DiffSeverity.Hard).ToList();
        if (hard.Count == 0)
        {
            return false;
        }
        Diverged(result, V2DivergenceKind.State, i, hard, d, d.PreState, simProjection);
        return true;
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

    private static JsonNode? SafeCombatProjection(CombatDriver driver)
    {
        try
        {
            return driver.State != null ? ProjectionJson.Write(SimProjector.Project(driver.State)) : null;
        }
        catch (Exception ex)
        {
            return JsonValue.Create($"<projection failed: {ex.GetType().Name}: {ex.Message}>");
        }
    }

    private static JsonNode? SafeRunProjection(RunSim sim)
    {
        try
        {
            return RunScreenProjector.Project(sim);
        }
        catch (Exception ex)
        {
            return JsonValue.Create($"<projection failed: {ex.GetType().Name}: {ex.Message}>");
        }
    }

    private static void CollectSegmentTouches(BundleSegment segment, SegmentResult result)
    {
        if (segment.RoomModelId != null)
        {
            string id = RunScreenProjector.StripCategory(segment.RoomModelId);
            result.SegmentTouched.Add(new ContentKey(ContentIndex.AreaOfRoomModel(id), id));
        }
        if (segment.CombatStart is { } shell)
        {
            result.SegmentTouched.Add(new ContentKey(ContentIndex.Encounter, shell.EncounterId));
            foreach (var relic in shell.Relics)
            {
                result.SegmentTouched.Add(new ContentKey(ContentIndex.Relic, relic));
            }
        }
    }

    private static void NoteTouches(SegmentResult result, SegmentDecision d)
    {
        var set = new HashSet<ContentKey>(result.SegmentTouched);
        ContentIndex.Collect(d.PreState, null, set);
        ContentIndex.Collect(d.Action.Raw, null, set);
        result.Touched[d.Index] = set;
    }
}
