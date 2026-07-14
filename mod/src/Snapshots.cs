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
/// (hash-deduped by the session, so redundant triggers are cheap).
///
/// Throttle (docs/design.md, "Snapshot throttling"): StateTracker bursts are
/// trailing-coalesced through a Core SnapshotThrottle with window
/// snapshot_min_interval_ms (config, default 150 ms) — a request inside the
/// window is never dropped, it defers ONE trailing snapshot via the
/// main-thread pump. Unconditional triggers (RunStarted header, CombatSetUp,
/// Turn/CombatEnded/Won, RoomEntered/Exited, post-action deferred) bypass the
/// throttle by calling TakeSnapshot directly.
/// </summary>
public static class Snapshots
{
    private static bool _staticSubscribed;
    private static bool _deferredPending;
    private static SnapshotThrottle? _throttle;

    private static SnapshotThrottle Throttle =>
        _throttle ??= new SnapshotThrottle(
            SystemClock.Instance, RecorderMod.SnapshotMinIntervalMs);

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
            () => CombatManager.Instance.CombatSetUp += _ => TakeSnapshot("phase"));
        RecorderMod.TrySubscribe("event:CombatManager.TurnStarted",
            () => CombatManager.Instance.TurnStarted += _ => TakeSnapshot("phase"));
        RecorderMod.TrySubscribe("event:CombatManager.TurnEnded",
            () => CombatManager.Instance.TurnEnded += _ => TakeSnapshot("phase"));
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
    /// Builds and records one snapshot now, bypassing the throttle (but still
    /// resetting its window). Never throws into game code; a builder failure
    /// is reported and the snapshot skipped. The builder is timed and reported
    /// into the session's perf counters (manifest "perf.snapshot_build_ms").
    /// </summary>
    public static void TakeSnapshot(string trigger)
    {
        try
        {
            var session = RecorderMod.Session;
            var runState = RecorderMod.CurrentRunState;
            if (session == null || runState == null)
            {
                return;
            }
            var stopwatch = Stopwatch.StartNew();
            PassiveStateBuilder.TryBuildSnapshot(runState, out var screen, out var state);
            stopwatch.Stop();
            session.RecordState(trigger, screen, state, stopwatch.Elapsed.TotalMilliseconds);
            Throttle.NoteSnapshotTaken();
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"Snapshots.TakeSnapshot:{trigger}", ex);
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
                    ScheduleTrailingSnapshot(trigger);
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

    private static void ScheduleTrailingSnapshot(string trigger)
    {
        RecorderMod.RunOnMainThread(() =>
        {
            if (Throttle.TryReleaseTrailing())
            {
                TakeSnapshot(trigger);
            }
            else if (Throttle.TrailingPending)
            {
                // Still inside the window: re-defer one more frame. If the
                // session ends meanwhile, TakeSnapshot no-ops harmlessly once
                // the window finally expires.
                ScheduleTrailingSnapshot(trigger);
            }
        });
    }

    /// <summary>
    /// Post-action snapshot one frame later (coalesced per frame) so the
    /// action's effects are visible in the recorded state.
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
            TakeSnapshot("action");
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
            TakeSnapshot("phase");
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
            TakeSnapshot("phase");
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
