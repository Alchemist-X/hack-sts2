using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// R1 / R2 / R11 writers: native/saves and native/replays copies, their
/// privileged events, and the unlock-profile event, driven through the
/// FakeGameScenario harness. When STS2REC_NATIVE_SESSION_OUT is set the
/// scripted session is written there for a manual `sts2rec validate`.
/// </summary>
public sealed class NativeAnchorTests
{
    private static string ResolveNativeOutputRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STS2REC_NATIVE_SESSION_OUT");
        return string.IsNullOrEmpty(fromEnv) ? NewTempRoot() : fromEnv;
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public void ScenarioArchivesEverySaveAndReplayWithMatchingEvents()
    {
        var run = FakeGameScenario.RunWithNativeAnchors(ResolveNativeOutputRoot());

        var states = ReadJsonl(run.SessionDir, "states");
        var actions = ReadJsonl(run.SessionDir, "actions");
        var events = ReadJsonl(run.SessionDir, "events");
        Assert.Equal(FakeGameScenario.NativeExpectedStates, states.Count);
        Assert.Equal(FakeGameScenario.NativeExpectedActions, actions.Count);
        Assert.Equal(FakeGameScenario.NativeExpectedEvents, events.Count);

        var seqs = AllSeqsInOrder(run.SessionDir);
        Assert.Equal(Enumerable.Range(1, seqs.Count).Select(n => (long)n), seqs);

        var anchors = events
            .Where(e => e.GetProperty("entry").GetString() is "run_saved" or "replay_archived")
            .ToList();
        Assert.Equal(3, anchors.Count);
        Assert.Equal(4, run.Receipts.Count);
        for (var i = 0; i < anchors.Count; i++)
        {
            var anchor = anchors[i];
            var receipt = run.Receipts[i];
            var data = anchor.GetProperty("data");
            var seq = anchor.GetProperty("seq").GetInt64();
            Assert.True(receipt.EventRecorded);
            Assert.Equal(seq, receipt.Seq);
            var file = data.GetProperty("file").GetString()!;
            Assert.Equal(receipt.RelativePath, file);
            Assert.EndsWith($"/{seq:D6}" + Path.GetExtension(file), file);
            var onDisk = File.ReadAllBytes(Path.Combine(run.SessionDir, file));
            Assert.Equal(run.SourceBytes[i], onDisk);
            Assert.Equal(onDisk.Length, data.GetProperty("bytes").GetInt32());
            Assert.Equal(Sha256Hex(onDisk), data.GetProperty("sha256").GetString());
        }

        var saves = Directory.GetFiles(Path.Combine(run.SessionDir, "native", "saves"));
        var replays = Directory.GetFiles(Path.Combine(run.SessionDir, "native", "replays"));
        Assert.Equal(FakeGameScenario.NativeExpectedSaves, saves.Length);
        Assert.Equal(FakeGameScenario.NativeExpectedReplays, replays.Length);
        Assert.DoesNotContain(saves.Concat(replays), path => path.EndsWith(".tmp", StringComparison.Ordinal));

        Assert.True(File.Exists(run.GameSavePath), "the game's save must be copied, never moved");
        Assert.True(File.Exists(run.GameReplayPath), "the game's replay must be copied, never moved");
        Assert.Equal(run.SourceBytes[1], File.ReadAllBytes(run.GameSavePath));
    }

    [Fact]
    public void RunSavedEventsCarryAnchorKindAndSaveHeader()
    {
        var run = FakeGameScenario.RunWithNativeAnchors(NewTempRoot());
        var mapStateSeq = ReadJsonl(run.SessionDir, "states")[0].GetProperty("seq").GetInt64();
        var saved = ReadJsonl(run.SessionDir, "events")
            .Where(e => e.GetProperty("entry").GetString() == "run_saved")
            .Select(e => e.GetProperty("data"))
            .ToList();
        Assert.Equal(2, saved.Count);

        var entry = saved[0];
        Assert.Equal("saved", entry.GetProperty("trigger").GetString());
        Assert.Equal("room_entry", entry.GetProperty("anchor_kind").GetString());
        Assert.False(entry.GetProperty("pre_finished").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("pre_finished_room_type").ValueKind);
        Assert.Equal(20, entry.GetProperty("schema_version").GetInt32());
        Assert.Equal(FakeGameScenario.StartTime, entry.GetProperty("start_time").GetInt64());
        Assert.Equal(0, entry.GetProperty("act").GetInt32());
        Assert.Equal(0, entry.GetProperty("floor").GetInt32());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("room_type").ValueKind);
        Assert.Equal(3, entry.GetProperty("map_coord").GetProperty("col").GetInt32());
        Assert.Equal(mapStateSeq, entry.GetProperty("state_seq").GetInt64());

        var exit = saved[1];
        Assert.Equal("combat_exit", exit.GetProperty("anchor_kind").GetString());
        Assert.True(exit.GetProperty("pre_finished").GetBoolean());
        Assert.Equal("monster", exit.GetProperty("pre_finished_room_type").GetString());
        Assert.Equal("Monster", exit.GetProperty("room_type").GetString());
        Assert.Equal(1, exit.GetProperty("floor").GetInt32());
        Assert.False(exit.TryGetProperty("parse_error", out _));
    }

    [Fact]
    public void ReplayWrittenAfterCompleteIsArchivedWithoutAnEvent()
    {
        var run = FakeGameScenario.RunWithNativeAnchors(NewTempRoot());
        var cleanUp = run.Receipts[^1];
        Assert.False(cleanUp.EventRecorded);
        var maxRecordSeq = AllSeqsInOrder(run.SessionDir).Max();
        Assert.True(cleanUp.Seq > maxRecordSeq);
        var path = Path.Combine(run.SessionDir, cleanUp.RelativePath);
        Assert.Equal(run.SourceBytes[^1], File.ReadAllBytes(path));
        Assert.Equal(Sha256Hex(run.SourceBytes[^1]), cleanUp.Sha256);

        var manifest = ReadManifest(run.SessionDir);
        Assert.False(manifest.GetProperty("incomplete").GetBoolean());
        Assert.Equal(
            FakeGameScenario.NativeExpectedEvents,
            manifest.GetProperty("counts").GetProperty("events").GetInt64());
    }

    [Fact]
    public void HiddenRunStateNeverReachesTheObservationOrActionStreams()
    {
        var run = FakeGameScenario.RunWithNativeAnchors(NewTempRoot());
        foreach (var stream in new[] { "states", "actions", "events" })
        {
            foreach (var line in ReadJsonlLines(run.SessionDir, stream))
            {
                Assert.DoesNotContain(FakeGameScenario.HiddenSeed, line, StringComparison.Ordinal);
            }
        }
        foreach (var line in ReadJsonlLines(run.SessionDir, "states"))
        {
            Assert.DoesNotContain("run_saved", line, StringComparison.Ordinal);
            Assert.DoesNotContain("unlock_profile", line, StringComparison.Ordinal);
            Assert.DoesNotContain("native/", line, StringComparison.Ordinal);
        }
        var saveCopy = Directory.GetFiles(Path.Combine(run.SessionDir, "native", "saves")).First();
        Assert.Contains(FakeGameScenario.HiddenSeed, File.ReadAllText(saveCopy), StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockProfileIsThePreludeEventWithSortedActs()
    {
        var run = FakeGameScenario.RunWithNativeAnchors(NewTempRoot());
        var first = ReadJsonl(run.SessionDir, "events")[0];
        Assert.Equal(1, first.GetProperty("seq").GetInt64());
        Assert.Equal("unlock_profile", first.GetProperty("entry").GetString());
        var data = first.GetProperty("data");
        Assert.Equal(
            new[] { "ACT.GLORY", "ACT.HIVE", "ACT.OVERGROWTH" },
            data.GetProperty("discovered_acts").EnumerateArray().Select(a => a.GetString()).ToArray());
        Assert.Equal(162, data.GetProperty("number_of_runs").GetInt32());
        Assert.Equal(1500, data.GetProperty("wongo_points").GetInt32());
        Assert.Equal(12, data.GetProperty("character_wins").GetInt32());
        Assert.Equal("SaveManager.Progress", data.GetProperty("source").GetString());
    }

    [Fact]
    public void ArchiveRejectsUnsafeCategoryAndExtension()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var bytes = new byte[] { 1, 2, 3 };
        Assert.Throws<ArgumentException>(() =>
            session.ArchiveNativeSnapshot("../saves", ".save", "run_saved", bytes, new JsonObject()));
        Assert.Throws<ArgumentException>(() =>
            session.ArchiveNativeSnapshot("saves", "save", "run_saved", bytes, new JsonObject()));
        Assert.Throws<ArgumentException>(() =>
            session.ArchiveNativeSnapshot("saves", "./x", "run_saved", bytes, new JsonObject()));
        Assert.Empty(ReadJsonl(session.Directory, "events"));
    }

    [Fact]
    public void ArchiveAfterDisposeThrowsObjectDisposed()
    {
        var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            session.ArchiveNativeSnapshot("saves", ".save", "run_saved", new byte[] { 1 }, new JsonObject()));
    }

    [Fact]
    public void ArchiveSeqInterleavesWithOtherRecordsAndCopiesTheCallerBytes()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var s1 = session.RecordState("phase", "map", new JsonObject { ["floor"] = 1 });
        var bytes = Encoding.UTF8.GetBytes("{\"schema_version\":20}");
        var receipt = session.ArchiveNativeSnapshot(
            "saves", ".save", "run_saved", bytes, new JsonObject { ["trigger"] = "saved" });
        bytes[0] = (byte)'X';
        var e1 = session.RecordEvent("card_drawn", new JsonObject());
        Assert.Equal(s1 + 1, receipt.Seq);
        Assert.Equal(receipt.Seq + 1, e1);
        Assert.True(session.WaitForNativeWrites(TimeSpan.FromSeconds(10)));
        var copied = File.ReadAllBytes(Path.Combine(session.Directory, receipt.RelativePath));
        Assert.Equal((byte)'{', copied[0]);
        Assert.Equal("native/saves/000002.save", receipt.RelativePath);
    }

    [Fact]
    public void WriterNeverOverwritesAnExistingFile()
    {
        var root = NewTempRoot();
        var target = Path.Combine(root, "native", "saves", "000001.save");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "original");
        var errors = new List<string>();
        using (var writer = new NativeFileWriter((where, ex) =>
               {
                   lock (errors)
                   {
                       errors.Add(where);
                   }
               }))
        {
            writer.Enqueue(target, Encoding.UTF8.GetBytes("replacement"));
            Assert.True(writer.WaitIdle(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, writer.Failed);
            Assert.Equal(0, writer.Written);
        }
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(errors);
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void WriterDrainsEveryQueuedJobOnDispose()
    {
        var root = NewTempRoot();
        var writer = new NativeFileWriter();
        const int jobs = 200;
        for (var i = 0; i < jobs; i++)
        {
            writer.Enqueue(Path.Combine(root, "replays", $"{i:D6}.mcr"), new byte[] { (byte)i, 7, 7 });
        }
        writer.Dispose();
        var files = Directory.GetFiles(Path.Combine(root, "replays"));
        Assert.Equal(jobs, files.Length);
        Assert.Equal(jobs, writer.Written);
        Assert.Throws<ObjectDisposedException>(() => writer.Enqueue(Path.Combine(root, "late.mcr"), new byte[1]));
    }

    [Fact]
    public void WriterRunsOffTheCallingThread()
    {
        var root = NewTempRoot();
        var blocker = Path.Combine(root, "blocked");
        File.WriteAllText(blocker, "a file where a directory is expected");
        var errorThread = -1;
        using var writer = new NativeFileWriter((_, _) => errorThread = Environment.CurrentManagedThreadId);
        writer.Enqueue(Path.Combine(blocker, "000001.save"), new byte[] { 1 });
        Assert.True(writer.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(-1, errorThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, errorThread);
    }

    [Theory]
    [InlineData(null, "room_entry")]
    [InlineData("monster", "combat_exit")]
    [InlineData("elite", "combat_exit")]
    [InlineData("boss", "combat_exit")]
    [InlineData("event", "ancient_exit")]
    [InlineData("treasure", "pre_finished")]
    public void SaveHeaderReaderClassifiesThePreFinishedRoom(string? roomType, string expectedKind)
    {
        var info = RunSaveInfo.Read(FakeGameScenario.SaveJson(roomType, numReloads: 4));
        Assert.Null(info.Error);
        Assert.Equal(20, info.SchemaVersion);
        Assert.Equal(FakeGameScenario.StartTime, info.StartTime);
        Assert.Equal(0, info.CurrentActIndex);
        Assert.Equal(4, info.NumReloads);
        Assert.Equal(roomType != null, info.HasPreFinishedRoom);
        Assert.Equal(roomType, info.PreFinishedRoomType);
        Assert.Equal(expectedKind, NativeAnchorEvents.AnchorKind(info));
    }

    [Fact]
    public void SaveHeaderReaderToleratesBomNullAndMalformedInput()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(FakeGameScenario.SaveJson(null)).ToArray();
        Assert.Equal(20, RunSaveInfo.Read(bom).SchemaVersion);

        var explicitNull = RunSaveInfo.Read(Encoding.UTF8.GetBytes(
            "{\"pre_finished_room\":null,\"schema_version\":20}"));
        Assert.False(explicitNull.HasPreFinishedRoom);
        Assert.Equal(20, explicitNull.SchemaVersion);

        var truncated = FakeGameScenario.SaveJson("monster");
        var broken = RunSaveInfo.Read(truncated.AsSpan(0, truncated.Length / 2));
        Assert.NotNull(broken.Error);
        Assert.Equal(20, broken.SchemaVersion);

        var notObject = RunSaveInfo.Read(Encoding.UTF8.GetBytes("[1,2]"));
        Assert.NotNull(notObject.Error);
        Assert.Null(notObject.StartTime);
    }

    [Theory]
    [InlineData("NEOW.pages.INITIAL.description", "INITIAL")]
    [InlineData("WELCOME_TO_WONGOS.pages.AFTER_BUY_RECEIVE_BADGE.description", "AFTER_BUY_RECEIVE_BADGE")]
    [InlineData("TRIAL.pages.DONE", "DONE")]
    [InlineData("NEOW.dialogue.3", null)]
    [InlineData("X.pages.", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PageKeyIsTheSegmentAfterPages(string? key, string? expected)
    {
        Assert.Equal(expected, LocKeys.PageKey(key));
    }

    [Fact]
    public void ReplayPayloadCarriesCleanUpFlagAndContext()
    {
        var data = NativeAnchorEvents.ReplayArchived(
            stopRecording: true, duringCleanUp: true,
            new AnchorContext(1, 20, "Elite", "ENCOUNTER.X", "Elite", 2, 4, 0));
        Assert.Equal("write_replay", data["trigger"]!.GetValue<string>());
        Assert.True(data["during_cleanup"]!.GetValue<bool>());
        Assert.Null(data["state_seq"]);
        Assert.Equal(20, data["floor"]!.GetValue<int>());
        Assert.Equal(4, data["map_coord"]!["row"]!.GetValue<int>());
    }
}
