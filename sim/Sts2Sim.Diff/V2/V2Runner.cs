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
            if (segment.FormatError == null && segment.UnavailableFields.Count > 0)
            {
                string reasons = segment.EligibilityReasons.Count > 0 ? string.Join(", ", segment.EligibilityReasons) : "segment marked ineligible";
                Skip(result, DivergenceClass.ExporterIneligible, "exporter ineligible (" + reasons + "); unavailable: " + string.Join(", ", segment.UnavailableFields));
                chainBroken = true;
                live = null;
                continue;
            }
            if (segment.FormatError != null)
            {
                string why = "unreadable segment: " + segment.FormatError;
                if (RngSetup.HasOldPrefix(run.Seed))
                {
                    Skip(result, DivergenceClass.VersionDrift, why);
                }
                else
                {
                    Skip(result, DivergenceClass.ExporterIneligible, why);
                }
                chainBroken = true;
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
        bool customWithoutModifiers = string.Equals(run.GameMode, "custom", StringComparison.Ordinal) && run.Modifiers.Count == 0
            && !run.RunReasons.Any(r => r.StartsWith("game_mode_modifiers", StringComparison.Ordinal));
        if (!string.Equals(run.GameMode, "standard", StringComparison.Ordinal) && !customWithoutModifiers)
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
            result.StartMode = run.FromV1 ? "v1" : segment.McrJoinedPart != null ? "mcr_join" : "v1_start";
            string? skip = BundleRunner.IneligibleReason(shell, options.IncludeIneligible);
            if (skip != null)
            {
                if (segment.McrJoinNote != null)
                {
                    Skip(result, DivergenceClass.AnchorError, segment.McrJoinNote + "; " + skip);
                    return null;
                }
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
                sim = BundleRunner.CreateSim(shell, StartExtras(segment));
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
            if (segment.McrJoinNote != null)
            {
                throw new StartFailure(DivergenceClass.AnchorError, segment.McrJoinNote);
            }
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

    // X3.1 combat start extras (sim/specs/diff-bundle-v2.md): potions [{slot,id}], max_potion_slots and gold from the
    // first battle snapshot; slots are set before the potions are added.
    public static CombatStartExtras StartExtras(BundleSegment segment)
    {
        var potions = RunBundleJson.StartPotions(segment)
            .Select(p => new KeyValuePair<int, string>(p.Slot, RunScreenProjector.StripCategory(p.Id)))
            .ToList();
        int? slots = segment.Start?["max_potion_slots"] is JsonValue sv && sv.TryGetValue(out int s) ? s : null;
        int? gold = segment.Start?["gold"] is JsonValue gv && gv.TryGetValue(out int g) ? g : null;
        return new CombatStartExtras(potions, slots, gold);
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
            catch (PlayerViewGapException ex)
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
                catch (PlayerViewGapException ex)
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
                    catch (PlayerViewGapException ex)
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
            V2Action applied = d.Action;
            try
            {
                var copies = d.Action.Type == V2Action.Choose
                    ? ResolveHandCopies(driver, d.Action, i + 1 < segment.Decisions.Count ? segment.Decisions[i + 1].PreState : null)
                    : null;
                var play = d.Action.Type == V2Action.PlayCard && IsMcrDecision(d)
                    ? ResolveMcrPlayIndex(driver, d.Action, i + 1 < segment.Decisions.Count ? segment.Decisions[i + 1] : null)
                    : null;
                if (play != null)
                {
                    result.McrPlayIndicesResolved++;
                    applied = play;
                }
                actionDiff = copies != null
                    ? ActionMapper.ApplyChoose(driver, copies, d.Action.Bool("skipped") == true, d.Action.Bool("confirm"))
                    : ActionMapper.ApplyCombat(driver, applied);
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
            result.ActionsPrefix.Add(applied.ToJson());
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
            // An abandoned combat's recording stops inside the combat (the part ended: a quit or a crashed game), so its
            // final_hp is the player HP of the last thing recorded (sts2rec diff_export._final_player_hp), not the HP
            // after the last decision the sim replays (farm 1MQXCR99GX f14, WATLF71NUH f22: the game died during the
            // last enemy turn). The outcome itself is still compared.
            bool abandoned = string.Equals(segment.OutcomeResult, DiffBundle.OutcomeAbandoned, StringComparison.Ordinal);
            if (segment.OutcomeFinalHp is int hp && hp != result.SimFinalHp && !abandoned)
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
                diffs.AddRange(CompareCombatSelect(oracleSelect, simSelect));
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
            if (d.Oracle?["rolled_forward"] is JsonValue rf && rf.TryGetValue(out bool rolled) && rolled)
            {
                diffs = diffs.Select(SoftenRolledForwardCounter).ToList();
            }
            return diffs.Where(x => x.Severity == DiffSeverity.Hard).Concat(diffs.Where(x => x.Severity == DiffSeverity.Soft)).ToList();
        }
        if (diffs.Count == 0 && simJson == null)
        {
            diffs.Add(new FieldDiff("pre_state", pre.DeepClone(), null, DiffSeverity.Hard, "a combat pre_state is a v1 projection or a 'select' projection"));
        }
        return diffs;
    }

    // Powers whose shown amount moves without a recorded event (SLOW_POWER per card in AfterCardPlayed,
    // Models.Powers/SlowPower.cs:32-38; HARDENED_SHELL_POWER per hit, HardenedShellPower.cs:25; TENDER_POWER per card,
    // TenderPower.cs:229). The exporter's roll-forward (`oracle.rolled_forward`, diff-bundle-v2.md) replays pending
    // events onto an older snapshot and cannot advance them, so on such pre_states their amount is soft.
    public static readonly IReadOnlySet<string> EventlessCounterPowers = new HashSet<string>(StringComparer.Ordinal)
    {
        "SLOW_POWER", "HARDENED_SHELL_POWER", "TENDER_POWER"
    };

    public const string RolledForwardCounterNote = "rolled-forward pre_state: the exporter cannot advance this event-less counter";

    public static FieldDiff SoftenRolledForwardCounter(FieldDiff diff)
    {
        if (diff.Severity != DiffSeverity.Hard || !diff.Path.EndsWith("].amount", StringComparison.Ordinal))
        {
            return diff;
        }
        int open = diff.Path.LastIndexOf("powers[", StringComparison.Ordinal);
        if (open < 0)
        {
            return diff;
        }
        string id = diff.Path[(open + "powers[".Length)..^"].amount".Length];
        return EventlessCounterPowers.Contains(id) ? diff with { Severity = DiffSeverity.Soft, Note = RolledForwardCounterNote } : diff;
    }

    public static bool IsMcrDecision(SegmentDecision d)
        => d.PreState == null && d.Oracle?["source"] is JsonValue v && v.TryGetValue(out string? s) && s == "mcr";

    private static string CardKey(CardInstance c) => c.IsUpgraded ? c.Id + "+" : c.Id;

    // W4.1 mcr hand indices by simulation (sim/specs/mcr-replay.md): the converter's hand index comes from the replay's
    // checksum states (none in v0.107.1, `hand_index_unresolved`) and takes the first of identical copies. The sim
    // replays from the decoded start, so the index is re-resolved against the sim hand: among the hand cards with the
    // recorded id and upgrade, the one whose play leaves the hand of the next decision's checksum state
    // (`oracle.checksum_state.hand`, ids with `+`) is taken, else the recorded index when it matches, else the first
    // match. Returns null when the recorded index stands.
    public static V2Action? ResolveMcrPlayIndex(CombatDriver driver, V2Action a, SegmentDecision? next)
    {
        if (driver.PendingChoice != null || driver.State is not { } cs || a.Str("card_id") is not { } id)
        {
            return null;
        }
        bool? upgraded = a.Bool("upgraded");
        var hand = cs.Player.Hand;
        var matches = new List<int>();
        for (int k = 0; k < hand.Count; k++)
        {
            if (hand[k].Id == id && (upgraded is not bool u || u == hand[k].IsUpgraded))
            {
                matches.Add(k);
            }
        }
        if (matches.Count == 0)
        {
            return null;
        }
        int recorded = a.Int("hand_index") ?? -1;
        int chosen = matches.Contains(recorded) ? recorded : matches[0];
        if (matches.Count > 1 && next?.Oracle?["checksum_state"]?["hand"] is JsonArray want)
        {
            var wanted = want.Select(x => x?.GetValue<string>()).ToList();
            foreach (int k in matches)
            {
                var trial = WithHandIndex(a, k);
                var clone = driver.Clone();
                if (ActionMapper.ApplyCombat(clone, trial) != null || clone.State == null)
                {
                    continue;
                }
                var after = clone.State.Player.Hand;
                if (after.Count == wanted.Count && Enumerable.Range(0, after.Count).All(j => CardKey(after[j]) == wanted[j]))
                {
                    chosen = k;
                    break;
                }
            }
        }
        return chosen == recorded ? null : WithHandIndex(a, chosen);
    }

    private static V2Action WithHandIndex(V2Action a, int index)
    {
        var raw = (JsonObject)a.Raw.DeepClone();
        raw["hand_index"] = index;
        return new V2Action(a.Type, raw, a.Picks);
    }

    public const int MaxHandCopyAssignments = 64;

    // Hand selects with identical copies: the recorder's pick index is into its UI-ordered candidate list, so which hand
    // copy was picked is not recorded (sim/specs/diff-report-w4-run.md T7 `choice identity`), but it decides the hand
    // order afterwards. The copy is the one whose pick leaves the sim hand in the order of the next observed pre_state;
    // without a next hand, or when no assignment reproduces it, the first unpicked copy is taken (null = no ambiguity).
    public static IReadOnlyList<ChoosePick>? ResolveHandCopies(CombatDriver driver, V2Action a, JsonNode? nextPreState)
    {
        if (!string.Equals(a.Str("kind"), "hand", StringComparison.Ordinal) || driver.PendingChoice is not { } prompt
            || nextPreState?["player"]?["hand"] is not JsonArray nextHand)
        {
            return null;
        }
        var picks = ActionMapper.HandPicksByIdentity(a);
        var options = prompt.Options;
        var perPick = new List<List<int>>(picks.Count);
        foreach (var p in picks)
        {
            string? id = p.CardId != null ? RunScreenProjector.StripCategory(p.CardId) : null;
            var matches = new List<int>();
            for (int k = 0; k < options.Count; k++)
            {
                if (id != null && string.Equals(options[k].ModelId, id, StringComparison.Ordinal) && (p.Upgraded is not bool up || up == options[k].UpgradeLevel > 0))
                {
                    matches.Add(k);
                }
            }
            if (matches.Count == 0)
            {
                return null;
            }
            perPick.Add(matches);
        }
        if (perPick.All(m => m.Count <= 1))
        {
            return null;
        }
        var want = nextHand.Select(c => (c?["id"]?.GetValue<string>(), c?["upgraded"] is JsonValue u && u.TryGetValue(out bool b) && b)).ToList();
        int tried = 0;
        foreach (var assignment in Assignments(perPick, 0, new List<int>()))
        {
            if (++tried > MaxHandCopyAssignments)
            {
                break;
            }
            var candidate = picks.Select((p, k) => p with { Index = assignment[k] }).ToList();
            var clone = driver.Clone();
            if (ActionMapper.ApplyChoose(clone, candidate, a.Bool("skipped") == true, a.Bool("confirm")) != null || clone.State == null)
            {
                continue;
            }
            var hand = clone.State.Player.Hand;
            if (hand.Count == want.Count && Enumerable.Range(0, want.Count).All(k => hand[k].Id == want[k].Item1 && hand[k].IsUpgraded == want[k].Item2))
            {
                return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<List<int>> Assignments(List<List<int>> perPick, int k, List<int> prefix)
    {
        if (k == perPick.Count)
        {
            yield return new List<int>(prefix);
            yield break;
        }
        foreach (int option in perPick[k])
        {
            if (prefix.Contains(option))
            {
                continue;
            }
            prefix.Add(option);
            foreach (var a in Assignments(perPick, k + 1, prefix))
            {
                yield return a;
            }
            prefix.RemoveAt(prefix.Count - 1);
        }
    }

    public static readonly string[] UnprojectedSelectFields = { "mode", "can_confirm", "screen_type", "can_skip", "can_cancel" };

    // Combat selection screens. `mode` / `can_confirm` and the candidates' `index` are recorder UI fields without a
    // sim projection (soft); so are the screen's class and button states: `screen_type` (the overlay type,
    // mod/src/StateBuilder/PassiveStateBuilder.Selections.cs:161-167, :278), `can_cancel` (the close / preview-cancel
    // button enabled, :221-245, :300) and `can_skip` (the skip button visible, :297). Whether a prompt can be skipped or
    // cancelled is checked by replaying the recorded answer (ApplyChoose). Hand selects list the candidates in UI order; the game's prompt is the hand in hand order,
    // filtered (Commands/CardSelectCmd.cs:3687), so their candidates compare as a multiset of (id, upgraded). Draw-pile
    // grids too: the rules order is rarity, then ModelId (Commands/CardSelectCmd.cs:897, :915), but the screen the recorder
    // reads sorts by rarity, then the localized title in the game's culture
    // (Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:412-430, Nodes.Cards/NCardGrid.cs:619), an order that
    // depends on the game's language (farm 3ZGJFKTD64 f9 STRATAGEM_POWER, a zh-CN game: same cards, other order).
    public static List<FieldDiff> CompareCombatSelect(JsonObject oracleSelect, JsonObject simSelect)
    {
        var o = (JsonObject)oracleSelect.DeepClone();
        var s = (JsonObject)simSelect.DeepClone();
        var soft = new List<FieldDiff>();
        foreach (var name in UnprojectedSelectFields)
        {
            if (o[name] is { } value)
            {
                soft.Add(new FieldDiff("select." + name, value.DeepClone(), null, DiffSeverity.Soft, RunScreenComparer.NotProjectedNote));
                o.Remove(name);
            }
        }
        bool hand = string.Equals(o["kind"]?.GetValue<string>(), "hand_select", StringComparison.Ordinal)
            || string.Equals(s["pile"]?.GetValue<string>(), "draw", StringComparison.Ordinal);
        foreach (var obj in new[] { o, s })
        {
            if (obj["candidates"] is not JsonArray arr)
            {
                continue;
            }
            var items = arr.Select(x => x!.DeepClone()).ToList();
            foreach (var item in items)
            {
                (item as JsonObject)?.Remove("index");
            }
            if (hand)
            {
                items = items.OrderBy(x => x!["id"]?.GetValue<string>(), StringComparer.Ordinal)
                    .ThenBy(x => x!["upgraded"]?.ToJsonString(), StringComparer.Ordinal).ToList();
            }
            obj["candidates"] = new JsonArray(items.ToArray());
        }
        var diffs = RunScreenComparer.Compare(new JsonObject { ["select"] = o }, new JsonObject { ["select"] = s });
        diffs.AddRange(soft);
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
