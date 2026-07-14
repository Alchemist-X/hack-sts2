using System;

namespace Sts2Recorder.Core;

/// <summary>Outcome of a throttled snapshot request.</summary>
public enum SnapshotRequestOutcome
{
    /// <summary>Outside the window: take the snapshot immediately.</summary>
    TakeNow,

    /// <summary>Inside the window, no trailing snapshot pending yet: the caller
    /// must schedule exactly one trailing snapshot (deferred via the main-thread
    /// pump) so the last state of a burst is never lost.</summary>
    Deferred,

    /// <summary>Inside the window with a trailing snapshot already pending:
    /// this request is coalesced into it, nothing to do.</summary>
    Coalesced,
}

/// <summary>
/// Trailing-coalesce throttle for StateTracker snapshot bursts (game-agnostic
/// so it is unit-testable without Godot). Semantics per docs/design.md:
/// a request within <c>minInterval</c> of the last taken snapshot is never
/// dropped — it is deferred into ONE trailing snapshot that fires once the
/// window has expired (the caller re-polls via <see cref="TryReleaseTrailing"/>
/// from the frame pump). Unconditional triggers bypass <see cref="Request"/>
/// entirely and report through <see cref="NoteSnapshotTaken"/> so they still
/// reset the window. A zero/negative interval disables throttling.
/// </summary>
public sealed class SnapshotThrottle
{
    private readonly object _lock = new();
    private readonly IClock _clock;
    private readonly double _minIntervalSeconds;
    private double _lastSnapshotAt = double.NegativeInfinity;
    private bool _trailingPending;

    public SnapshotThrottle(IClock clock, double minIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _minIntervalSeconds = Math.Max(0, minIntervalMs) / 1000.0;
    }

    /// <summary>True while a trailing snapshot is scheduled but not yet released.</summary>
    public bool TrailingPending
    {
        get
        {
            lock (_lock)
            {
                return _trailingPending;
            }
        }
    }

    /// <summary>Classifies a throttleable snapshot request (see enum docs).</summary>
    public SnapshotRequestOutcome Request()
    {
        lock (_lock)
        {
            if (_trailingPending)
            {
                return SnapshotRequestOutcome.Coalesced;
            }
            if (_clock.Now - _lastSnapshotAt >= _minIntervalSeconds)
            {
                return SnapshotRequestOutcome.TakeNow;
            }
            _trailingPending = true;
            return SnapshotRequestOutcome.Deferred;
        }
    }

    /// <summary>
    /// Records that a snapshot was actually taken (throttled or bypass),
    /// resetting the window that throttled requests are measured against.
    /// </summary>
    public void NoteSnapshotTaken()
    {
        lock (_lock)
        {
            _lastSnapshotAt = _clock.Now;
        }
    }

    /// <summary>
    /// Called from the deferred pump callback. True = the window expired and
    /// the trailing snapshot must be taken now; false with
    /// <see cref="TrailingPending"/> still true = re-defer one more frame.
    /// </summary>
    public bool TryReleaseTrailing()
    {
        lock (_lock)
        {
            if (!_trailingPending)
            {
                return false;
            }
            if (_clock.Now - _lastSnapshotAt < _minIntervalSeconds)
            {
                return false;
            }
            _trailingPending = false;
            return true;
        }
    }
}
