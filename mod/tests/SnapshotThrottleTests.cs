using Sts2Recorder.Core;
using Xunit;

namespace Sts2Recorder.Core.Tests;

/// <summary>Manually stepped clock (no auto-advance) for throttle timing tests.</summary>
public sealed class ManualClock : IClock
{
    public double Now { get; set; }

    public ManualClock(double start) => Now = start;

    public void Advance(double seconds) => Now += seconds;
}

public sealed class SnapshotThrottleTests
{
    private const double Start = 1000.0;

    private static (SnapshotThrottle Throttle, ManualClock Clock) Make(double intervalMs = 150)
    {
        var clock = new ManualClock(Start);
        return (new SnapshotThrottle(clock, intervalMs), clock);
    }

    [Fact]
    public void FirstRequestIsTakenImmediately()
    {
        var (throttle, _) = Make();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
    }

    [Fact]
    public void BurstInsideWindowCoalescesIntoOneTrailingSnapshot()
    {
        var (throttle, clock) = Make();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
        throttle.NoteSnapshotTaken();

        clock.Advance(0.020);
        Assert.Equal(SnapshotRequestOutcome.Deferred, throttle.Request());
        clock.Advance(0.020);
        Assert.Equal(SnapshotRequestOutcome.Coalesced, throttle.Request());
        Assert.Equal(SnapshotRequestOutcome.Coalesced, throttle.Request());
        Assert.True(throttle.TrailingPending);

        // Pump fires while still inside the window: re-defer, never drop.
        Assert.False(throttle.TryReleaseTrailing());
        Assert.True(throttle.TrailingPending);

        // Window expires: the ONE trailing snapshot is released.
        clock.Advance(0.150);
        Assert.True(throttle.TryReleaseTrailing());
        Assert.False(throttle.TrailingPending);
        // No spurious second release.
        Assert.False(throttle.TryReleaseTrailing());
    }

    [Fact]
    public void RequestOutsideWindowIsTakenNow()
    {
        var (throttle, clock) = Make();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
        throttle.NoteSnapshotTaken();
        clock.Advance(0.151);
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
    }

    [Fact]
    public void UnthrottledSnapshotResetsTheWindow()
    {
        var (throttle, clock) = Make();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
        throttle.NoteSnapshotTaken();
        clock.Advance(0.140);
        // A bypass trigger (e.g. RoomEntered) takes a snapshot and resets the window...
        throttle.NoteSnapshotTaken();
        clock.Advance(0.020);
        // ...so 160 ms after the first snapshot is still inside the new window.
        Assert.Equal(SnapshotRequestOutcome.Deferred, throttle.Request());
    }

    [Fact]
    public void TrailingReleaseWaitsForWindowAfterBypassSnapshot()
    {
        var (throttle, clock) = Make();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
        throttle.NoteSnapshotTaken();
        clock.Advance(0.050);
        Assert.Equal(SnapshotRequestOutcome.Deferred, throttle.Request());
        // Bypass snapshot lands while the trailing one is pending.
        clock.Advance(0.050);
        throttle.NoteSnapshotTaken();
        // 150 ms after the ORIGINAL snapshot but only 60 ms after the bypass:
        clock.Advance(0.060);
        Assert.False(throttle.TryReleaseTrailing());
        clock.Advance(0.100);
        Assert.True(throttle.TryReleaseTrailing());
    }

    [Fact]
    public void ZeroIntervalDisablesThrottling()
    {
        var (throttle, _) = Make(intervalMs: 0);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
            throttle.NoteSnapshotTaken();
        }
    }

    [Fact]
    public void NegativeIntervalIsClampedToZero()
    {
        var (throttle, _) = Make(intervalMs: -100);
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
        throttle.NoteSnapshotTaken();
        Assert.Equal(SnapshotRequestOutcome.TakeNow, throttle.Request());
    }
}
