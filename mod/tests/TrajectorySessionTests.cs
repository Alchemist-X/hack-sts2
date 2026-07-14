using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

public sealed class TrajectorySessionTests
{
    private static JsonNode State(int floor, int energy) =>
        new JsonObject { ["screen"] = "combat", ["floor"] = floor, ["energy"] = energy };

    [Fact]
    public void EnvelopeAndGlobalSeqMonotonicAcrossStreams()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s1 = session.RecordState("phase", "combat", State(1, 3));
        var e1 = session.RecordEvent("card_drawn", new JsonObject { ["card"] = "CARD.ZAP" });
        var a1 = session.RecordAction(
            "hook:PlayCardPatch", "play_card",
            new JsonObject { ["card"] = "CARD.ZAP" }, "executed", s1);
        var e2 = session.RecordEvent("damage_dealt", new JsonObject { ["amount"] = 8 });
        session.Flush();

        Assert.Equal(new[] { 1L, 2L, 3L, 4L }, new[] { s1, e1, a1, e2 });

        var states = ReadJsonl(session.Directory, "states");
        var actions = ReadJsonl(session.Directory, "actions");
        var events = ReadJsonl(session.Directory, "events");
        Assert.Single(states);
        Assert.Single(actions);
        Assert.Equal(2, events.Count);

        var state = states[0];
        Assert.Equal(1, state.GetProperty("seq").GetInt64());
        Assert.Equal("state", state.GetProperty("type").GetString());
        Assert.Equal("phase", state.GetProperty("trigger").GetString());
        Assert.Equal("combat", state.GetProperty("screen").GetString());
        Assert.Equal(16, state.GetProperty("hash").GetString()!.Length);
        Assert.Equal(1, state.GetProperty("state").GetProperty("floor").GetInt32());

        var action = actions[0];
        Assert.Equal("action", action.GetProperty("type").GetString());
        Assert.Equal("hook:PlayCardPatch", action.GetProperty("source").GetString());
        Assert.Equal("play_card", action.GetProperty("action").GetProperty("kind").GetString());
        Assert.Equal(
            "CARD.ZAP",
            action.GetProperty("action").GetProperty("params").GetProperty("card").GetString());
        Assert.Equal("executed", action.GetProperty("status").GetString());
        Assert.Equal(1, action.GetProperty("state_seq").GetInt64());

        Assert.Equal("event", events[0].GetProperty("type").GetString());
        Assert.Equal("card_drawn", events[0].GetProperty("entry").GetString());

        var all = AllSeqsInOrder(session.Directory);
        Assert.Equal(new[] { 1L, 2L, 3L, 4L }, all);
    }

    [Fact]
    public void HashDedupReturnsPriorSeqWithoutWriting()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var first = session.RecordState("phase", "combat", State(1, 3));
        var duplicate = session.RecordState("poll", "combat", State(1, 3));
        var third = session.RecordState("action", "combat", State(1, 2));
        session.Flush();

        Assert.Equal(first, duplicate);
        Assert.Equal(third, session.LatestStateSeq);
        Assert.True(third > first);
        Assert.Equal(2, ReadJsonl(session.Directory, "states").Count);
        // dedup must not consume a seq number
        Assert.Equal(first + 1, third);
    }

    [Fact]
    public void DedupOnlyAppliesToConsecutiveIdenticalStates()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var a = session.RecordState("phase", "combat", State(1, 3));
        var b = session.RecordState("action", "combat", State(1, 2));
        var c = session.RecordState("action", "combat", State(1, 3)); // same as first, not consecutive
        Assert.True(a < b && b < c);
        session.Flush();
        Assert.Equal(3, ReadJsonl(session.Directory, "states").Count);
    }

    [Fact]
    public void LatestStateSeqIsZeroBeforeFirstSnapshot()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        Assert.Equal(0, session.LatestStateSeq);
        var seq = session.RecordState("phase", "combat", State(1, 3));
        Assert.Equal(seq, session.LatestStateSeq);
    }

    [Fact]
    public void NullStateSeqUsesLatestSnapshot()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s = session.RecordState("phase", "combat", State(1, 3));
        session.RecordAction("hook:EndTurnPatch", "end_turn", new JsonObject(), "executed", null);
        session.Flush();
        var action = ReadJsonl(session.Directory, "actions").Single();
        Assert.Equal(s, action.GetProperty("state_seq").GetInt64());
    }

    [Fact]
    public void UnknownActionStatusIsRejected()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        Assert.Throws<ArgumentException>(() =>
            session.RecordAction("hook:X", "play_card", new JsonObject(), "pending", null));
    }

    [Fact]
    public void FlushRewritesManifestAtomicallyWithCurrentCounts()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));

        var initial = ReadManifest(session.Directory);
        Assert.True(initial.GetProperty("incomplete").GetBoolean());
        Assert.Equal(0, initial.GetProperty("counts").GetProperty("states").GetInt64());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("result").ValueKind);

        var s = session.RecordState("phase", "combat", State(1, 3));
        session.RecordEvent("card_drawn", new JsonObject { ["card"] = "CARD.ZAP" });
        session.RecordAction("hook:X", "play_card", new JsonObject(), "executed", s);
        session.Flush();

        var manifest = ReadManifest(session.Directory);
        Assert.Equal(1, manifest.GetProperty("schema_version").GetInt32());
        Assert.True(manifest.GetProperty("incomplete").GetBoolean());
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("states").GetInt64());
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("actions").GetInt64());
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("events").GetInt64());
        Assert.Equal(Seed, manifest.GetProperty("run").GetProperty("seed").GetString());
        Assert.Equal(StartTime, manifest.GetProperty("run").GetProperty("start_time").GetInt64());
        Assert.Equal(1, manifest.GetProperty("part").GetInt32());
        Assert.False(File.Exists(Path.Combine(session.Directory, "manifest.json.tmp")));
    }

    [Fact]
    public void CompleteFinalizesManifest()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s = session.RecordState("phase", "combat", State(1, 3));
        session.RecordAction("hook:X", "end_turn", new JsonObject(), "executed", s);
        session.Complete(new RunResult(Win: true, Abandoned: false, EndTime: StartTime + 100.0));

        var manifest = ReadManifest(session.Directory);
        Assert.False(manifest.GetProperty("incomplete").GetBoolean());
        var result = manifest.GetProperty("result");
        Assert.True(result.GetProperty("win").GetBoolean());
        Assert.False(result.GetProperty("abandoned").GetBoolean());
        Assert.Equal(StartTime + 100.0, result.GetProperty("end_time").GetDouble());
        Assert.Throws<InvalidOperationException>(
            () => session.Complete(new RunResult(false, true, StartTime + 101.0)));
    }

    [Fact]
    public void PartSuffixOnResume()
    {
        var root = NewTempRoot();
        var meta = DefaultMeta();
        string firstDir;
        using (var first = TrajectorySession.Begin(root, meta, new FakeClock(StartTime)))
        {
            firstDir = first.Directory;
        }
        using var second = TrajectorySession.Begin(root, meta, new FakeClock(StartTime + 50));
        using var third = TrajectorySession.Begin(root, meta, new FakeClock(StartTime + 90));

        Assert.Equal($"{StartTime}-{Seed}", Path.GetFileName(firstDir));
        Assert.Equal($"{StartTime}-{Seed}-part2", Path.GetFileName(second.Directory));
        Assert.Equal($"{StartTime}-{Seed}-part3", Path.GetFileName(third.Directory));
        Assert.Equal(1, ReadManifest(firstDir).GetProperty("part").GetInt32());
        Assert.Equal(2, ReadManifest(second.Directory).GetProperty("part").GetInt32());
        Assert.Equal(3, ReadManifest(third.Directory).GetProperty("part").GetInt32());
    }

    [Fact]
    public void DegradedHooksLandInManifestDeduplicated()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.AddDegradedHook("hook:MapPatch");
        session.AddDegradedHook("hook:ShopPatch");
        session.AddDegradedHook("hook:MapPatch"); // duplicate ignored

        var hooks = ReadManifest(session.Directory)
            .GetProperty("degraded_hooks")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        Assert.Equal(new[] { "hook:MapPatch", "hook:ShopPatch" }, hooks);
    }

    [Fact]
    public void ArchiveNativeCopiesWithoutMoving()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "1773034386.run");
        File.WriteAllText(source, "{\"floor\": 12}");
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.ArchiveNative(source, "run_history.run");

        var copied = Path.Combine(session.Directory, "native", "run_history.run");
        Assert.True(File.Exists(source), "source must still exist (copy, not move)");
        Assert.True(File.Exists(copied));
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(copied));
    }

    [Fact]
    public void RecordErrorIsRateLimitedPerSite()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        for (var i = 0; i < 50; i++)
        {
            session.RecordError("hook:PlayCardPatch", new InvalidOperationException($"boom {i}"));
        }
        session.RecordError("hook:MapPatch", new InvalidOperationException("other site"));

        var lines = File.ReadAllLines(Path.Combine(session.Directory, "recorder.log"));
        var playCardLines = lines.Count(line => line.Contains("hook:PlayCardPatch"));
        // 5 entries + 1 suppression notice
        Assert.Equal(ErrorLog.MaxEntriesPerSite + 1, playCardLines);
        Assert.Contains(lines, line => line.Contains("suppressed"));
        Assert.Contains(lines, line => line.Contains("hook:MapPatch"));
    }

    [Fact]
    public void DisposeWithoutCompleteLeavesManifestIncomplete()
    {
        var root = NewTempRoot();
        string dir;
        using (var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime)))
        {
            var s = session.RecordState("phase", "combat", State(1, 3));
            session.RecordAction("hook:X", "play_card", new JsonObject(), "executed", s);
            dir = session.Directory;
        }
        var manifest = ReadManifest(dir);
        Assert.True(manifest.GetProperty("incomplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("result").ValueKind);
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("states").GetInt64());
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("actions").GetInt64());
    }

    [Fact]
    public void RecordAfterDisposeThrows()
    {
        var root = NewTempRoot();
        var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        session.Dispose();
        session.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(
            () => session.RecordEvent("card_drawn", new JsonObject()));
    }
}
