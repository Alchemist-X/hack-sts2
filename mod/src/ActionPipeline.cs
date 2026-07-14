using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

/// <summary>
/// The action spine: a prefix on ActionQueueSynchronizer.RequestEnqueue (the
/// umbrella funnel every locally-initiated synced action passes through) plus
/// the provisional-until-executed rule — every enqueued action is finalized via
/// GameAction.BeforeExecuted / BeforeCancelled and only then recorded, with the
/// status ("executed"/"cancelled") the game itself decided.
///
/// End-turn gap fix (2026-07-14, level-2 verification): programmatic end-turns
/// (STS2MCP's PlayerCmd.EndTurn, AutoSlay, VoidForm, CreatureCmd forced ends,
/// dead-player auto-ready) NEVER enqueue an EndPlayerTurnAction — PlayerCmd.EndTurn
/// (PlayerCmd.cs:279) calls CombatManager.SetReadyToEndTurn directly, so the
/// RequestEnqueue funnel cannot see them (only the automatic
/// ReadyToBeginEnemyTurnAction that follows). The authoritative end-turn tap is
/// therefore the native CombatManager.PlayerEndedTurn event (fired inside
/// SetReadyToEndTurn, CombatManager.cs:694, both paths), deduped against the
/// enqueue-funnel record via a flag raised while EndPlayerTurnAction.ExecuteAction
/// runs (the human-button path, which the funnel already records).
/// </summary>
public static class ActionPipeline
{
    private sealed class PendingAction
    {
        public required string Kind;
        public required JsonObject Params;
        public required long StateSeq;
        public required string Source;
        public bool Finalized;
    }

    /// <summary>
    /// Dedupe by object identity: actions deferred during the enemy turn are
    /// re-passed through RequestEnqueue at PlayPhase start.
    /// </summary>
    private static ConditionalWeakTable<GameAction, PendingAction> _pending = new();

    private static string? _humanScopeSource;

    /// <summary>
    /// True while EndPlayerTurnAction.ExecuteAction runs (main thread; the game
    /// action executor is single-threaded). SetReadyToEndTurn — and therefore
    /// the PlayerEndedTurn event — fires synchronously inside that window for
    /// the enqueued (human button / FTUE) path, which the RequestEnqueue funnel
    /// already recorded; the event tap skips those to keep one record per
    /// end-turn decision.
    /// </summary>
    private static bool _endPlayerTurnActionExecuting;

    // Private-field readers for action params (guarded; null when a game update renames them).
    // v0.107.1: end-turn actions renamed _combatRound -> _turnNumber (per-player turn counter),
    // vote source is the new MapLocation struct (was RunLocation), pick-relic index is int?
    // (null = explicit treasure-relic skip via TreasureRoomRelicSynchronizer.SkipRelicLocally).
    private static AccessTools.FieldRef<EndPlayerTurnAction, int>? _endTurnNumber;
    private static AccessTools.FieldRef<UndoEndPlayerTurnAction, int>? _undoEndTurnNumber;
    private static AccessTools.FieldRef<DiscardPotionGameAction, uint>? _discardSlotIndex;
    private static AccessTools.FieldRef<VoteForMapCoordAction, MapLocation>? _voteSource;
    private static AccessTools.FieldRef<VoteForMapCoordAction, MapVote?>? _voteDestination;
    private static AccessTools.FieldRef<MoveToMapCoordAction, MegaCrit.Sts2.Core.Map.MapCoord>? _moveDestination;
    private static AccessTools.FieldRef<PickRelicAction, int?>? _pickRelicIndex;

    internal static void ApplyPatches(Harmony harmony)
    {
        _endTurnNumber = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<EndPlayerTurnAction, int>("_turnNumber"));
        _undoEndTurnNumber = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<UndoEndPlayerTurnAction, int>("_turnNumber"));
        _discardSlotIndex = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<DiscardPotionGameAction, uint>("_potionSlotIndex"));
        _voteSource = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<VoteForMapCoordAction, MapLocation>("_source"));
        _voteDestination = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<VoteForMapCoordAction, MapVote?>("_destination"));
        _moveDestination = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<MoveToMapCoordAction, MegaCrit.Sts2.Core.Map.MapCoord>("_destination"));
        _pickRelicIndex = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<PickRelicAction, int?>("_relicIndex"));

        RecorderMod.TryPatch(
            harmony,
            "patch:ActionQueueSynchronizer.RequestEnqueue",
            () => AccessTools.Method(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue)),
            prefix: new HarmonyMethod(typeof(ActionPipeline), nameof(RequestEnqueuePrefix)));

        // End-turn gap fix (2026-07-14): mark the EndPlayerTurnAction execution
        // window so the PlayerEndedTurn tap below can dedupe against the
        // enqueue-funnel record (see class doc). Prefix/finalizer pair — a throw
        // inside ExecuteAction cannot leave the flag stuck.
        RecorderMod.TryPatch(
            harmony,
            "patch:EndPlayerTurnAction.ExecuteAction",
            () => AccessTools.Method(typeof(EndPlayerTurnAction), "ExecuteAction"),
            prefix: new HarmonyMethod(typeof(ActionPipeline), nameof(EndPlayerTurnExecutePrefix)),
            finalizer: new HarmonyMethod(typeof(ActionPipeline), nameof(EndPlayerTurnExecuteFinalizer)));

        // CombatManager.Instance is an eager static singleton (CombatManager.cs:80)
        // — one static subscription, the handler no-ops without a session (same
        // pattern as EventTap.SubscribeStaticEvents).
        RecorderMod.TrySubscribe(
            "event:CombatManager.PlayerEndedTurn",
            () => CombatManager.Instance.PlayerEndedTurn += OnPlayerEndedTurn);
    }

    internal static void OnRunStarted()
    {
        _pending = new ConditionalWeakTable<GameAction, PendingAction>();
        _humanScopeSource = null;
        _endPlayerTurnActionExecuting = false;
    }

    internal static void OnRunEnding()
    {
        _pending = new ConditionalWeakTable<GameAction, PendingAction>();
        _humanScopeSource = null;
        _endPlayerTurnActionExecuting = false;
    }

    /// <summary>
    /// Opens a human-attribution scope: any action enqueued while the scope is
    /// active (i.e. synchronously inside the patched UI handler) is tagged as a
    /// genuine human input with the given source. Always paired with
    /// <see cref="EndHumanScope"/> in a Harmony finalizer so a throw cannot leak
    /// the scope onto a later programmatic action.
    /// </summary>
    internal static void BeginHumanScope(string source)
    {
        _humanScopeSource = source;
    }

    internal static void EndHumanScope()
    {
        _humanScopeSource = null;
    }

    private static void RequestEnqueuePrefix(GameAction action)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null || action == null)
            {
                return;
            }
            if (_pending.TryGetValue(action, out _))
            {
                return; // Deferred-action re-entry at PlayPhase start; already tracked.
            }

            var (kind, parameters) = Describe(action);
            var human = _humanScopeSource != null;
            parameters["human"] = human;
            var pending = new PendingAction
            {
                Kind = kind,
                Params = parameters,
                StateSeq = session.LatestStateSeq,
                Source = _humanScopeSource ?? "hook:ActionQueueSynchronizer.RequestEnqueue",
            };
            _pending.Add(action, pending);

            // Provisional-until-executed: finalize via the action's own lifecycle events.
            action.BeforeExecuted += a => Finalize(a, "executed");
            action.BeforeCancelled += a => Finalize(a, "cancelled");
        }
        catch (Exception ex)
        {
            RecorderMod.Report("ActionPipeline.RequestEnqueuePrefix", ex);
        }
    }

    private static void Finalize(GameAction action, string status)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null || !_pending.TryGetValue(action, out var pending) || pending.Finalized)
            {
                return;
            }
            pending.Finalized = true;
            session.RecordAction(pending.Source, pending.Kind, pending.Params, status, pending.StateSeq);
            if (status == "executed")
            {
                // One frame later so state_after exists for canonical alignment.
                Snapshots.RequestDeferredSnapshot();
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("ActionPipeline.Finalize", ex);
        }
    }

    private static void EndPlayerTurnExecutePrefix()
    {
        _endPlayerTurnActionExecuting = true;
    }

    private static void EndPlayerTurnExecuteFinalizer()
    {
        _endPlayerTurnActionExecuting = false;
    }

    /// <summary>
    /// Authoritative end-turn tap: fires inside CombatManager.SetReadyToEndTurn
    /// (after its already-ready dedupe check) for BOTH the enqueued human-button
    /// path and every programmatic PlayerCmd.EndTurn caller (STS2MCP, AutoSlay,
    /// VoidForm, CreatureCmd, dead-player auto-ready at turn start). The
    /// enqueued path is skipped via <see cref="_endPlayerTurnActionExecuting"/>
    /// — the RequestEnqueue funnel already recorded it with human attribution —
    /// so this records exactly the end-turns the funnel cannot see. Fires for
    /// remote players too in co-op; filtered to the local player.
    /// </summary>
    private static void OnPlayerEndedTurn(Player player, bool canBackOut)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null || _endPlayerTurnActionExecuting)
            {
                return;
            }
            if (!JsonDescribe.Try<bool?>(() => LocalContext.IsMe(player)).GetValueOrDefault())
            {
                return;
            }
            var parameters = new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(() => (long)player.NetId),
                ["turn_number"] = JsonDescribe.Try<int?>(() => player.PlayerCombatState?.TurnNumber),
                // false for every programmatic caller today; recorded for drift visibility.
                ["can_back_out"] = canBackOut,
                // No enqueued action => no UI handler scope => programmatic
                // (the human button always goes through the enqueue path above).
                ["human"] = _humanScopeSource != null,
            };
            // "committed": the game has already marked the player ready — there is
            // no cancellable GameAction lifecycle on this path (an undo would be a
            // separate first-class undo_end_turn record).
            session.RecordAction(
                "event:CombatManager.PlayerEndedTurn", "end_turn", parameters,
                "committed", session.LatestStateSeq);
            Snapshots.RequestDeferredSnapshot();
        }
        catch (Exception ex)
        {
            RecorderMod.Report("ActionPipeline.OnPlayerEndedTurn", ex);
        }
    }

    /// <summary>Per-type kind + params snapshot (live references read immediately).</summary>
    private static (string Kind, JsonObject Params) Describe(GameAction action)
    {
        switch (action)
        {
            case PlayCardAction play:
                return ("play_card", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)play.Player.NetId),
                    ["card_model_id"] = JsonDescribe.Try(() => play.CardModelId.ToString()),
                    ["card_name"] = JsonDescribe.Try(() => play.CardModelId.Entry),
                    ["combat_card_index"] = JsonDescribe.Try<long?>(() => play.NetCombatCard.CombatCardIndex),
                    ["target_id"] = JsonDescribe.Try<long?>(() => play.TargetId),
                    ["target"] = JsonDescribe.Try(() => JsonDescribe.Creature(play.Target)),
                });
            case UsePotionAction use:
                return ("use_potion", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)use.Player.NetId),
                    ["potion_index"] = JsonDescribe.Try<long?>(() => use.PotionIndex),
                    ["potion_id"] = JsonDescribe.Try(
                        () => use.Player.GetPotionAtSlotIndex((int)use.PotionIndex)?.Id.ToString()),
                    ["target_id"] = JsonDescribe.Try<long?>(() => use.TargetId),
                    ["in_combat"] = JsonDescribe.Try<bool?>(() => use.WasEnqueuedInCombat),
                });
            case EndPlayerTurnAction end:
                // v0.107.1: per-player turn counter (PlayerCombatState.TurnNumber), not
                // the shared combat round; key renamed accordingly.
                return ("end_turn", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)end.OwnerId),
                    ["turn_number"] = _endTurnNumber != null
                        ? JsonDescribe.Try<int?>(() => _endTurnNumber(end))
                        : null,
                });
            case UndoEndPlayerTurnAction undo:
                return ("undo_end_turn", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)undo.OwnerId),
                    ["turn_number"] = _undoEndTurnNumber != null
                        ? JsonDescribe.Try<int?>(() => _undoEndTurnNumber(undo))
                        : null,
                });
            case DiscardPotionGameAction discard:
                return ("discard_potion", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)discard.OwnerId),
                    ["potion_index"] = _discardSlotIndex != null
                        ? JsonDescribe.Try<long?>(() => _discardSlotIndex(discard))
                        : null,
                    ["in_combat"] = JsonDescribe.Try<bool?>(() => discard.WasEnqueuedInCombat),
                });
            case VoteForMapCoordAction vote:
                return ("vote_for_map_coord", DescribeMapVote(vote));
            case MoveToMapCoordAction move:
                return ("move_to_map_coord", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)move.OwnerId),
                    ["destination"] = _moveDestination != null
                        ? JsonDescribe.Try(() => Coord(_moveDestination(move)))
                        : null,
                });
            case PickRelicAction pick:
                return ("pick_relic", DescribePickRelic(pick));
            case VoteToMoveToNextActAction next:
                return ("vote_to_move_to_next_act", new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)next.OwnerId),
                });
            default:
                return (JsonDescribe.SnakeCase(TrimActionSuffix(action.GetType().Name)), new JsonObject
                {
                    ["player"] = JsonDescribe.Try<long?>(() => (long)action.OwnerId),
                    ["action_type"] = action.GetType().Name,
                });
        }
    }

    private static JsonObject DescribeMapVote(VoteForMapCoordAction vote)
    {
        var obj = new JsonObject
        {
            ["player"] = JsonDescribe.Try<long?>(() => (long)vote.OwnerId),
        };
        if (_voteSource != null)
        {
            obj["source"] = JsonDescribe.Try(() =>
            {
                var source = _voteSource(vote);
                return (JsonNode?)new JsonObject
                {
                    ["act_index"] = source.actIndex,
                    ["coord"] = source.coord.HasValue ? Coord(source.coord.Value) : null,
                };
            });
        }
        if (_voteDestination != null)
        {
            obj["destination"] = JsonDescribe.Try(() =>
            {
                var destination = _voteDestination(vote);
                return destination.HasValue ? Coord(destination.Value.coord) : null;
            });
        }
        return obj;
    }

    private static JsonObject DescribePickRelic(PickRelicAction pick)
    {
        var obj = new JsonObject
        {
            ["player"] = JsonDescribe.Try<long?>(() => (long)pick.OwnerId),
        };
        if (_pickRelicIndex != null)
        {
            // v0.107.1: _relicIndex is int?; null = the player explicitly SKIPPED the
            // treasure relic (TreasureRoomRelicSynchronizer.SkipRelicLocally -> PickRelicLocally(null)).
            int? index = null;
            var indexRead = false;
            try
            {
                index = _pickRelicIndex(pick);
                indexRead = true;
            }
            catch
            {
                // Field read failed; leave relic_index null without claiming a skip.
            }
            obj["relic_index"] = index;
            if (indexRead)
            {
                obj["skipped"] = !index.HasValue;
            }
            if (index.HasValue)
            {
                obj["relic_id"] = JsonDescribe.Try(() =>
                {
                    var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
                    return relics != null && index.Value >= 0 && index.Value < relics.Count
                        ? relics[index.Value].Id.ToString()
                        : null;
                });
            }
        }
        return obj;
    }

    private static JsonNode Coord(MegaCrit.Sts2.Core.Map.MapCoord coord)
    {
        return new JsonObject
        {
            ["col"] = coord.col,
            ["row"] = coord.row,
        };
    }

    private static string TrimActionSuffix(string typeName)
    {
        const string suffix = "Action";
        return typeName.EndsWith(suffix, StringComparison.Ordinal) && typeName.Length > suffix.Length
            ? typeName[..^suffix.Length]
            : typeName;
    }
}
