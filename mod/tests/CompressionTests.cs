using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Gzip-at-Complete contract: streams become .jsonl.gz with byte-identical
/// content, plain files are deleted only on verified success, crash-style
/// (never-completed) sessions stay plain, and the manifest carries
/// compression + perf.
/// </summary>
public sealed class CompressionTests
{
    private static JsonNode State(int floor, int energy) =>
        new JsonObject { ["screen"] = "combat", ["floor"] = floor, ["energy"] = energy };

    private static byte[] Gunzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void CompleteCompressesStreamsByteIdentically()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s1 = session.RecordState("phase", "combat", State(1, 3), buildMillis: 2.5);
        session.RecordAction(
            "hook:PlayCardPatch", "play_card",
            new JsonObject { ["card"] = "CARD.ZAP" }, "executed", s1);
        for (var i = 0; i < 200; i++)
        {
            session.RecordEvent("card_drawn", new JsonObject { ["card"] = $"CARD.{i}" });
        }
        session.RecordState("action", "combat", State(1, 2), buildMillis: 4.0);
        session.Flush();

        // Snapshot the exact pre-compression bytes for the identity check.
        var plainBytes = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (var stream in new[] { "states", "actions", "events" })
        {
            plainBytes[stream] = File.ReadAllBytes(
                Path.Combine(session.Directory, $"{stream}.jsonl"));
        }

        session.Complete(new RunResult(Win: true, Abandoned: false, EndTime: StartTime + 60.0));

        foreach (var stream in new[] { "states", "actions", "events" })
        {
            var plain = Path.Combine(session.Directory, $"{stream}.jsonl");
            var compressed = plain + ".gz";
            Assert.False(File.Exists(plain), $"{stream}.jsonl must be deleted after verified compression");
            Assert.True(File.Exists(compressed), $"{stream}.jsonl.gz must exist");
            Assert.Equal(plainBytes[stream], Gunzip(compressed));
        }
        // manifest.json and native/ stay plain.
        Assert.True(File.Exists(Path.Combine(session.Directory, "manifest.json")));

        var manifest = ReadManifest(session.Directory);
        Assert.Equal("gz", manifest.GetProperty("compression").GetString());
        Assert.False(manifest.GetProperty("incomplete").GetBoolean());

        var perf = manifest.GetProperty("perf");
        var build = perf.GetProperty("snapshot_build_ms");
        Assert.Equal(2, build.GetProperty("count").GetInt64());
        Assert.Equal(3.25, build.GetProperty("avg").GetDouble(), precision: 3);
        Assert.Equal(4.0, build.GetProperty("max").GetDouble(), precision: 3);
        var bytesWritten = perf.GetProperty("bytes_written");
        Assert.Equal(plainBytes["states"].Length, bytesWritten.GetProperty("states").GetInt64());
        Assert.Equal(plainBytes["actions"].Length, bytesWritten.GetProperty("actions").GetInt64());
        Assert.Equal(plainBytes["events"].Length, bytesWritten.GetProperty("events").GetInt64());
    }

    [Fact]
    public void FakeGameScenarioSessionRoundTripsLineIdentically()
    {
        var root = NewTempRoot();
        var sessionDir = FakeGameScenario.Run(root);

        foreach (var stream in new[] { "states", "actions", "events" })
        {
            Assert.False(File.Exists(Path.Combine(sessionDir, $"{stream}.jsonl")));
            Assert.True(File.Exists(Path.Combine(sessionDir, $"{stream}.jsonl.gz")));
        }
        // Reopen through the harness (gz-aware) and verify line-level integrity.
        Assert.Equal(FakeGameScenario.ExpectedStates, ReadJsonl(sessionDir, "states").Count);
        Assert.Equal(FakeGameScenario.ExpectedActions, ReadJsonl(sessionDir, "actions").Count);
        Assert.Equal(FakeGameScenario.ExpectedEvents, ReadJsonl(sessionDir, "events").Count);
        foreach (var line in ReadJsonlLines(sessionDir, "events"))
        {
            Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(line).RootElement.ValueKind);
        }
    }

    [Fact]
    public void DisposeWithoutCompleteLeavesPlainStreams()
    {
        var root = NewTempRoot();
        string dir;
        using (var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime)))
        {
            session.RecordState("phase", "combat", State(1, 3));
            dir = session.Directory;
        }
        // Crash-terminated sessions (no Complete) stay plain by design.
        foreach (var stream in new[] { "states", "actions", "events" })
        {
            Assert.True(File.Exists(Path.Combine(dir, $"{stream}.jsonl")));
            Assert.False(File.Exists(Path.Combine(dir, $"{stream}.jsonl.gz")));
        }
        var manifest = ReadManifest(dir);
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("compression").ValueKind);
    }

    [Fact]
    public void CompressorFailureKeepsPlainFilesAndReportsError()
    {
        var root = NewTempRoot();
        var sessionDir = Path.Combine(root, "broken-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "states.jsonl"), "{\"seq\":1}\n");
        File.WriteAllText(Path.Combine(sessionDir, "actions.jsonl"), "{\"seq\":2}\n");
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{\"seq\":3}\n");
        // A directory squats on the .gz target: compressing events must fail.
        Directory.CreateDirectory(Path.Combine(sessionDir, "events.jsonl.gz"));

        string? reportedWhere = null;
        var ok = SessionCompressor.TryCompressStreams(
            sessionDir, (where, ex) => reportedWhere = where);

        Assert.False(ok);
        Assert.NotNull(reportedWhere);
        Assert.True(File.Exists(Path.Combine(sessionDir, "states.jsonl")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "actions.jsonl")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "events.jsonl")));
        // No half-compressed leftovers.
        Assert.False(File.Exists(Path.Combine(sessionDir, "states.jsonl.gz")));
        Assert.False(File.Exists(Path.Combine(sessionDir, "actions.jsonl.gz")));
    }

    [Fact]
    public void RecordsAfterCompleteAreDroppedNotWritten()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.RecordState("phase", "combat", State(1, 3));
        session.Complete(new RunResult(Win: false, Abandoned: false, EndTime: StartTime + 10.0));

        // Late-firing handlers between Complete and Dispose must not corrupt
        // the finalized (compressed) session.
        Assert.Equal(0, session.RecordEvent("card_drawn", new JsonObject()));
        Assert.Equal(0, session.RecordAction(
            "hook:X", "end_turn", new JsonObject(), "executed", null));
        session.RecordState("phase", "combat", State(2, 3));

        Assert.Single(ReadJsonl(session.Directory, "states"));
        Assert.Empty(ReadJsonl(session.Directory, "actions"));
        Assert.Empty(ReadJsonl(session.Directory, "events"));
        var counts = ReadManifest(session.Directory).GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("states").GetInt64());
        Assert.Equal(0, counts.GetProperty("actions").GetInt64());
        Assert.Equal(0, counts.GetProperty("events").GetInt64());
        // The drop is visible in recorder.log (rate-limited, first drop only).
        var log = File.ReadAllText(Path.Combine(session.Directory, "recorder.log"));
        Assert.Contains("after Complete", log);
    }
}
