using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// RequestFlush contract: the save-heartbeat flush (queue drain + fsync +
/// manifest rewrite) runs on a background flusher thread so the game thread
/// never blocks on it. Requests coalesce; the manifest written by an async
/// flush must never claim lines that have not physically reached disk; and the
/// terminal manifests (Complete/Dispose) always win over an in-flight async
/// flush — a stale incomplete manifest may never overwrite a final one.
/// </summary>
public sealed class AsyncFlushTests
{
    /// <summary>Upper bound for any async-flush condition; generous for loaded CI machines.</summary>
    private const int PollTimeoutMs = 5000;

    private const int PollIntervalMs = 10;

    /// <summary>
    /// How long a stale flusher rewrite gets to (wrongly) surface after a
    /// terminal manifest before we declare terminal-wins verified.
    /// </summary>
    private const int StaleRewriteGraceMs = 300;

    private static JsonNode State(int n) =>
        new JsonObject { ["screen"] = "combat", ["n"] = n };

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < PollTimeoutMs)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(PollIntervalMs);
        }
        Assert.Fail($"timed out waiting for {what}");
    }

    private static long ManifestCount(string dir, string stream)
    {
        var manifest = ReadManifest(dir);
        return manifest.GetProperty("counts").GetProperty(stream).GetInt64();
    }

    private static int DiskLines(string dir, string stream) =>
        ReadJsonlLines(dir, stream).Count;

    [Fact]
    public void RequestFlush_EventuallyRewritesManifestWithFlushedCounts()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        for (var i = 0; i < 3; i++)
        {
            session.RecordState("phase", "combat", State(i));
        }
        session.RecordAction("hook:Test", "play_card", new JsonObject(), "executed", null);
        session.RecordEvent("card_drawn", new JsonObject());

        session.RequestFlush();

        WaitUntil(
            () => ManifestCount(session.Directory, "states") == 3
                  && ManifestCount(session.Directory, "actions") == 1
                  && ManifestCount(session.Directory, "events") == 1,
            "async manifest to reflect all recorded lines");
        Assert.Equal(3, DiskLines(session.Directory, "states"));
    }

    [Fact]
    public void RequestFlush_ManifestNeverClaimsMoreLinesThanOnDisk()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        for (var i = 0; i < 5; i++)
        {
            session.RecordState("phase", "combat", State(i));
        }
        session.RequestFlush();
        // Race more records against the in-flight flush; the invariant must hold
        // at every observable moment.
        for (var i = 5; i < 10; i++)
        {
            session.RecordState("phase", "combat", State(i));
            session.RequestFlush();
        }

        WaitUntil(
            () =>
            {
                var claimed = ManifestCount(session.Directory, "states");
                Assert.True(
                    claimed <= DiskLines(session.Directory, "states"),
                    $"manifest claims {claimed} states, disk has fewer");
                return claimed == 10;
            },
            "async manifest to catch up to 10 states");
    }

    [Fact]
    public void RequestFlush_CoalescesManyRequestsWithoutErrors()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        for (var i = 0; i < 50; i++)
        {
            session.RecordState("phase", "combat", State(i));
            session.RequestFlush();
        }
        WaitUntil(
            () => ManifestCount(session.Directory, "states") == 50,
            "coalesced flushes to converge on the final counts");
        Assert.False(
            File.Exists(Path.Combine(session.Directory, "recorder.log")),
            "async flusher reported errors to recorder.log");
    }

    [Fact]
    public void Complete_AfterPendingRequestFlush_FinalManifestWins()
    {
        var root = NewTempRoot();
        var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        try
        {
            session.RecordState("phase", "combat", State(1));
            session.RequestFlush();
            session.Complete(new RunResult(Win: true, Abandoned: false, EndTime: StartTime + 100));

            var manifest = ReadManifest(session.Directory);
            Assert.False(manifest.GetProperty("incomplete").GetBoolean());
            Assert.True(manifest.GetProperty("result").GetProperty("win").GetBoolean());

            // A stale async rewrite would surface shortly after; the final
            // manifest must still be in place once the flusher has settled.
            Thread.Sleep(StaleRewriteGraceMs);
            manifest = ReadManifest(session.Directory);
            Assert.False(manifest.GetProperty("incomplete").GetBoolean());
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public void Dispose_WithPendingRequestFlush_IsCleanAndIdempotent()
    {
        var root = NewTempRoot();
        var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "combat", State(1));
        session.RequestFlush();
        session.Dispose();
        session.Dispose();
        session.RequestFlush();

        var manifest = ReadManifest(session.Directory);
        Assert.True(manifest.GetProperty("incomplete").GetBoolean());
        Assert.Equal(1, ManifestCount(session.Directory, "states"));
        Assert.Empty(Directory.GetFiles(session.Directory, "*.tmp"));
    }

    [Fact]
    public void SynchronousFlush_StillBlocksUntilManifestIsCurrent()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "combat", State(1));
        session.RecordState("phase", "combat", State(2));
        session.Flush();
        Assert.Equal(2, ManifestCount(session.Directory, "states"));
        Assert.Equal(2, DiskLines(session.Directory, "states"));
    }

    /// <summary>
    /// 0.3.0 save heartbeat order (RunLifecycle.OnSaved): native save capture
    /// first, then the async flush request — the published cut must cover the
    /// capture's run_saved event.
    /// </summary>
    [Fact]
    public void RequestFlush_AfterNativeCapture_ManifestCoversItsEvent()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "map", State(1));
        session.ArchiveNativeSnapshot(
            "saves", ".save", "run_saved", new byte[] { 1, 2, 3 }, new JsonObject { ["trigger"] = "saved" });

        session.RequestFlush();

        WaitUntil(
            () => ManifestCount(session.Directory, "events") == 1
                  && ManifestCount(session.Directory, "states") == 1,
            "async manifest to cover the native capture event");
        Assert.Equal(1, DiskLines(session.Directory, "events"));
    }

    /// <summary>
    /// Teardown with a native copy queued and an async flush in flight: Dispose
    /// must stop the flusher, synchronously flush every record enqueued so far
    /// (manifest counts == disk lines), and drain the native writer.
    /// </summary>
    [Fact]
    public void Dispose_WithNativeCaptureAndPendingRequestFlush_LosesNothing()
    {
        var root = NewTempRoot();
        var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "map", State(1));
        var payload = new byte[64 * 1024];
        new Random(7).NextBytes(payload);
        var receipt = session.ArchiveNativeSnapshot(
            "saves", ".save", "run_saved", payload, new JsonObject { ["trigger"] = "saved" });
        session.RequestFlush();
        for (var i = 0; i < 20; i++)
        {
            session.RecordEvent("card_drawn", new JsonObject { ["i"] = i });
        }
        session.RecordAction("hook:Test", "play_card", new JsonObject(), "executed", null);
        session.RecordState("action", "combat", State(2));

        session.Dispose();

        var manifest = ReadManifest(session.Directory);
        Assert.True(manifest.GetProperty("incomplete").GetBoolean());
        foreach (var stream in new[] { "states", "actions", "events" })
        {
            Assert.Equal(DiskLines(session.Directory, stream), ManifestCount(session.Directory, stream));
        }
        Assert.Equal(2, ManifestCount(session.Directory, "states"));
        Assert.Equal(1, ManifestCount(session.Directory, "actions"));
        Assert.Equal(21, ManifestCount(session.Directory, "events"));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(session.Directory, receipt.RelativePath)));
        Assert.Empty(Directory.GetFiles(session.Directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Flush_SkipsManifestRewriteWhenNothingNewSinceLastFlush()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "combat", State(1));
        session.Flush();
        var manifestPath = Path.Combine(session.Directory, "manifest.json");
        var mtime = File.GetLastWriteTimeUtc(manifestPath);
        var content = File.ReadAllText(manifestPath);

        session.Flush();

        Assert.Equal(mtime, File.GetLastWriteTimeUtc(manifestPath));
        Assert.Equal(content, File.ReadAllText(manifestPath));
    }
}
