using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HarmonyLib;
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
    }

    internal static void OnRunStarted()
    {
        _pending = new ConditionalWeakTable<GameAction, PendingAction>();
        _humanScopeSource = null;
    }

    internal static void OnRunEnding()
    {
        _pending = new ConditionalWeakTable<GameAction, PendingAction>();
        _humanScopeSource = null;
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
