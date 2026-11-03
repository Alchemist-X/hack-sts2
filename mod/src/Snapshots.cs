using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// State snapshot triggers per docs/hook-map.md. Primary trigger is the game's
/// own debounced dirty signal (StateTracker.CombatStateChanged), paired with
/// unconditional CombatEnded/CombatWon snapshots because the tracker silently
/// drops empty-state callbacks. Post-action snapshots are deferred one frame
/// via the ProcessFrame pump so state_after exists for canonical alignment.
/// All snapshots go through PassiveStateBuilder.TryBuildSnapshot + RecordState
/// (dedup saves serialization/I/O, but building a redundant state still costs CPU).
///
/// Throttle (docs/design.md, "Snapshot throttling"): StateTracker bursts are
/// trailing-coalesced through a Core SnapshotThrottle with window
/// snapshot_min_interval_ms (config, default 150 ms) — a request inside the
/// window is never dropped, it defers ONE trailing snapshot via the
/// main-thread pump.
///
/// Burst coalescing: phase triggers (CombatSetUp, Turn boundaries,
/// RoomEntered/Exited) and the post-action deferred snapshot go through a
/// second, SHORT trailing-coalesce window (burst_min_interval_ms, default
/// 100 ms) — adjacent triggers (TurnEnded+TurnStarted, rapid card plays) merge
/// into one trailing build instead of paying a full main-thread state build
/// each. The includeDeck request of CombatSetUp survives coalescing (OR-ed over
/// every request merged into the pending snapshot). Truly terminal triggers
/// (RunStarted header, CombatEnded/Won, run end) still bypass everything and
/// build immediately: their snapshot must exist before Complete()/teardown can
/// run.
/// </summary>
public static class Snapshots
{
    private static bool _staticSubscribed;
    private static bool _deferredPending;
    // Label for the pending coalesced snapshot: the LATEST deferring/coalescing
    // requester wins, because the merged trailing snapshot represents the
    // settled state after the last trigger (e.g. "action" when a card play
    // folded into a pending phase trailing).
    private static string _burstTrigger = "phase";
    // includeDeck for the pending coalesced snapshot: OR over every request
    // merged into it (CombatSetUp asks for the master deck; a later TurnStarted
    // folded into the same trailing must not drop it). Consumed when the
    // coalesced snapshot is taken; cleared on cancel. Main-thread only.
    private static bool _burstIncludeDeck;
    private static SnapshotThrottle? _throttle;
    private static SnapshotThrottle? _burstThrottle;

    private static SnapshotThrottle Throttle =>
        _throttle ??= new SnapshotThrottle(
            SystemClock.Instance, RecorderMod.SnapshotMinIntervalMs);

    private static SnapshotThrottle BurstThrottle =>
        _burstThrottle ??= new SnapshotThrottle(
            SystemClock.Instance, RecorderMod.BurstMinIntervalMs);

    /// <summary>
    /// RunManager.Instance and CombatManager.Instance are eager static
    /// singletons — these subscriptions are allowed at init time and survive
    /// across runs; every handler no-ops when no session is active.
    /// </summary>
    internal static void SubscribeStaticEvents()
    {
        if (_staticSubscribed)
        {
            return;
        }
        _staticSubscribed = true;

        // NotifyCombatStateChanged THROWS if anything is subscribed while
        // TestMode.IsOn — never subscribe under TestMode (mods do not load in
        // TestMode, but the guard is cheap insurance).
        if (!TestMode.IsOn)
        {
            RecorderMod.TrySubscribe("event:CombatStateTracker.CombatStateChanged",
                () => CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged);
        }
        RecorderMod.TrySubscribe("event:CombatManager.CombatSetUp",
            () => CombatManager.Instance.CombatSetUp += _ => RequestCoalescedSnapshot("phase", includeDeck: true));
        RecorderMod.TrySubscribe("event:CombatManager.TurnStarted",
            () => CombatManager.Instance.TurnStarted += _ => RequestCoalescedSnapshot("phase"));
        RecorderMod.TrySubscribe("event:CombatManager.TurnEnded",
            () => CombatManager.Instance.TurnEnded += _ => RequestCoalescedSnapshot("phase"));
        // Unconditional end-of-combat snapshots: the StateTracker drops snapshots
        // when creatures are empty, and CombatEnded is the only signal on losses.
        RecorderMod.TrySubscribe("event:CombatManager.CombatEnded",
            () => CombatManager.Instance.CombatEnded += _ => TakeSnapshot("phase"));
        RecorderMod.TrySubscribe("event:CombatManager.CombatWon",
            () => CombatManager.Instance.CombatWon += _ => TakeSnapshot("phase"));
        RecorderMod.TrySubscribe("event:RunManager.RoomEntered",
            () => RunManager.Instance.RoomEntered += OnRoomEntered);
        RecorderMod.TrySubscribe("event:RunManager.RoomExited",
            () => RunManager.Instance.RoomExited += OnRoomExited);
        RecorderMod.TrySubscribe("event:RunManager.ActEntered",
            () => RunManager.Instance.ActEntered += OnActEntered);
    }

    /// <summary>
    /// Builds and records one snapshot now, bypassing the throttles (but still
    /// resetting both windows). Never throws into game code; a builder failure
    /// is reported and the snapshot skipped. The whole main-thread cost — state
    /// build plus the session's canonical hash and envelope serialization — is
    /// timed and reported into the session's perf counters (manifest
    /// "perf.snapshot_build_ms").
    /// <paramref name="includeDeck"/> adds the master deck to an in-combat
    /// snapshot (combat setup); non-combat snapshots always carry it.
    /// </summary>
    public static void TakeSnapshot(string trigger, bool includeDeck = false)
    {
        CaptureSnapshot(trigger, includeDeck);
    }

    /// <summary>
    /// Decision capture (recorder 0.4.0): builds the pre-choice snapshot NOW,
    /// synchronously, bypassing both throttle windows and the burst coalescing,
    /// and pins its seq to the decision — the action must reference this exact
    /// observation, never a later trailing one. <paramref name="screen"/> is the
    /// UI owner of the choice (null = whatever the builder sees on top). Returns
    /// null without a session; a failed build yields StateSeq 0, which the
    /// action records as <c>_decision.status: "missing"</c>.
    /// </summary>
    internal static DecisionCapture? CaptureDecision(object? screen, string stage = "before_input")
    {
        var session = RecorderMod.Session;
        if (session == null) return null;
        return new DecisionCapture(session, CaptureSnapshot("decision:" + stage, includeDeck: false, screen), stage);
    }

    private static long CaptureSnapshot(string trigger, bool includeDeck, object? decisionScreen = null)
    {
        try
        {
            var session = RecorderMod.Session;
            var runState = RecorderMod.CurrentRunState;
            if (session == null || runState == null)
            {
                return 0;
            }
            var stopwatch = Stopwatch.StartNew();
            bool built = PassiveStateBuilder.TryBuildSnapshot(
                runState, includeDeck, out var screen, out var state, decisionScreen);
            long seq = session.RecordState(trigger, screen, state);
            stopwatch.Stop();
            session.RecordSnapshotCost(stopwatch.Elapsed.TotalMilliseconds, trigger);
            // Any snapshot resets BOTH windows, so a poll or phase trigger right
            // after a forced snapshot coalesces instead of building again.
            Throttle.NoteSnapshotTaken();
            BurstThrottle.NoteSnapshotTaken();
            return built ? seq : 0;
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"Snapshots.TakeSnapshot:{trigger}", ex);
            return 0;
        }
    }

    /// <summary>
    /// Throttled snapshot entry point (StateTracker bursts). Inside the window
    /// the request is trailing-coalesced: exactly one deferred snapshot is
    /// scheduled via the main-thread pump and re-deferred frame-by-frame until
    /// the window expires — the last state of a burst is never lost.
    /// </summary>
    internal static void RequestThrottledSnapshot(string trigger)
    {
        try
        {
            switch (Throttle.Request())
            {
                case SnapshotRequestOutcome.TakeNow:
                    TakeSnapshot(trigger);
                    break;
                case SnapshotRequestOutcome.Deferred:
                    ScheduleTrailingSnapshot(Throttle, static () => ("poll", false));
                    break;
                case SnapshotRequestOutcome.Coalesced:
                    break;
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"Snapshots.RequestThrottledSnapshot:{trigger}", ex);
        }
    }

    /// <summary>
    /// Burst-coalesced snapshot entry point for non-terminal phase triggers.
    /// Same trailing-coalesce contract as <see cref="RequestThrottledSnapshot"/>
    /// but over the short burst window, so adjacent phase events (TurnEnded +
    /// TurnStarted) cost one state build instead of one each. The final state of
    /// a burst is never lost — only intermediate states inside the window merge.
    /// <paramref name="includeDeck"/> is OR-ed into the pending snapshot, so a
    /// coalesced CombatSetUp still records the master deck.
    /// </summary>
    internal static void RequestCoalescedSnapshot(string trigger, bool includeDeck = false)
    {
        try
        {
            _burstTrigger = trigger;
            _burstIncludeDeck |= includeDeck;
            switch (BurstThrottle.Request())
            {
                case SnapshotRequestOutcome.TakeNow:
                {
                    var (takeTrigger, takeDeck) = ConsumeBurstRequest();
                    TakeSnapshot(takeTrigger, takeDeck);
                    break;
                }
                case SnapshotRequestOutcome.Deferred:
                    // Trigger + includeDeck resolved at release time (latest
                    // requester's label, OR of every merged includeDeck).
                    ScheduleTrailingSnapshot(BurstThrottle, static () => ConsumeBurstRequest());
                    break;
                case SnapshotRequestOutcome.Coalesced:
                    break;
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"Snapshots.RequestCoalescedSnapshot:{trigger}", ex);
        }
    }

    /// <summary>Reads and resets the pending coalesced snapshot's label + includeDeck.</summary>
    private static (string Trigger, bool IncludeDeck) ConsumeBurstRequest()
    {
        var includeDeck = _burstIncludeDeck;
        _burstIncludeDeck = false;
        return (_burstTrigger, includeDeck);
    }

    /// <summary>
    /// Session teardown hook, called from RunLifecycle BEFORE the session is
    /// detached. A trailing snapshot still pending in either window (or the
    /// one-frame action defer) would otherwise be lost on save-and-quit — no
    /// terminal fires on that path — so its final state is captured now,
    /// bypassing the windows. Cancelling first also stops the orphaned pump
    /// callbacks from building against the next run's session.
    /// </summary>
    internal static void FlushPendingSnapshot()
    {
        try
        {
            var pollPending = Throttle.CancelTrailing();
            var burstTrailing = BurstThrottle.CancelTrailing();
            var actionDeferred = _deferredPending;
            _deferredPending = false;
            var (burstTrigger, burstDeck) = ConsumeBurstRequest();
            var session = RecorderMod.Session;
            if ((pollPending || burstTrailing || actionDeferred) && session is { IsCompleted: false })
            {
                // Label by which window was actually pending: a pure poll
                // trailing must not inherit the stale burst trigger, and a
                // one-frame action defer that never reached the burst window
                // is an "action" snapshot.
                var trigger = burstTrailing ? burstTrigger : actionDeferred ? "action" : "poll";
                TakeSnapshot(trigger, includeDeck: burstTrailing && burstDeck);
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("Snapshots.FlushPendingSnapshot", ex);
        }
    }

    /// <summary>
    /// Cancels pending trailing work without building — for the terminal path,
    /// where the forced run-end snapshot that follows captures the final state.
    /// </summary>
    internal static void CancelPendingSnapshots()
    {
        Throttle.CancelTrailing();
        BurstThrottle.CancelTrailing();
        _burstIncludeDeck = false;
        _deferredPending = false;
    }

    private static void ScheduleTrailingSnapshot(
        SnapshotThrottle throttle, Func<(string Trigger, bool IncludeDeck)> release)
    {
        RecorderMod.RunOnMainThread(() =>
        {
            if (throttle.TryReleaseTrailing())
            {
                var (trigger, includeDeck) = release();
                TakeSnapshot(trigger, includeDeck);
            }
            else if (throttle.TrailingPending)
            {
                // Still inside the window: re-defer one more frame. If the
                // session ends meanwhile, TakeSnapshot no-ops harmlessly once
                // the window finally expires.
                ScheduleTrailingSnapshot(throttle, release);
            }
        });
    }

    /// <summary>
    /// Post-action snapshot one frame later (coalesced per frame, then through
    /// the burst window) so the action's effects are visible in the recorded
    /// state and rapid action sequences merge into one build.
    /// </summary>
    public static void RequestDeferredSnapshot()
    {
        if (_deferredPending)
        {
            return;
        }
        _deferredPending = true;
        RecorderMod.RunOnMainThread(() =>
        {
            _deferredPending = false;
            RequestCoalescedSnapshot("action");
        });
    }

    private static void OnCombatStateChanged(CombatState state)
    {
        RequestThrottledSnapshot("poll");
    }

    private static void OnRoomEntered()
    {
        try
        {
            var session = RecorderMod.Session;
            var runState = RecorderMod.CurrentRunState;
            if (session != null && runState != null)
            {
                session.RecordEvent("room_entered", new JsonObject
                {
                    ["room_type"] = JsonDescribe.Try(() => runState.CurrentRoom?.RoomType.ToString()),
                    ["room_model_id"] = JsonDescribe.Try(() => runState.CurrentRoom?.ModelId?.ToString()),
                    ["act"] = JsonDescribe.Try<int?>(() => runState.CurrentActIndex),
                    ["floor"] = JsonDescribe.Try<int?>(() => runState.TotalFloor),
                });
            }
            RequestCoalescedSnapshot("phase");
        }
        catch (Exception ex)
        {
            RecorderMod.Report("Snapshots.OnRoomEntered", ex);
        }
    }

    /// <summary>
    /// Harvests the just-finished floor's decisions/deltas: the game's own
    /// MapPointHistoryEntry has no Changed event, so RoomExited is the snapshot
    /// point (docs/hook-map.md section 6).
    /// </summary>
    private static void OnRoomExited()
    {
        try
        {
            var session = RecorderMod.Session;
            var runState = RecorderMod.CurrentRunState;
            if (session != null && runState != null)
            {
                var entry = runState.CurrentMapPointHistoryEntry;
                if (entry != null)
                {
                    session.RecordEvent("floor_summary", SerializeMapPointEntry(entry));
                }
            }
            RequestCoalescedSnapshot("phase");
        }
        catch (Exception ex)
        {
            RecorderMod.Report("Snapshots.OnRoomExited", ex);
        }
    }

    private static void OnActEntered()
    {
        try
        {
            var session = RecorderMod.Session;
            var runState = RecorderMod.CurrentRunState;
            if (session != null && runState != null)
            {
                session.RecordEvent("act_entered", new JsonObject
                {
                    ["act"] = JsonDescribe.Try<int?>(() => runState.CurrentActIndex),
                });
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("Snapshots.OnActEntered", ex);
        }
    }

    /// <summary>
    /// MapPointHistoryEntry is System.Text.Json-annotated; serialize with the
    /// game's own options (custom LocString/variable converters) and fall back
    /// to a minimal summary if the contract changes in a future build.
    /// </summary>
    private static JsonNode SerializeMapPointEntry(MapPointHistoryEntry entry)
    {
        try
        {
            var json = JsonSerializer.Serialize(entry, entry.GetType(), JsonSerializationUtility.Options);
            return JsonNode.Parse(json) ?? new JsonObject();
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["serialize_error"] = ex.GetType().Name + ": " + ex.Message,
                ["map_point_type"] = JsonDescribe.Try(() => entry.MapPointType.ToString()),
                ["rooms"] = JsonDescribe.Try<int?>(() => entry.Rooms.Count),
            };
        }
    }
}
