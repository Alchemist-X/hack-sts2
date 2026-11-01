using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sts2Recorder.Core;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// THE SANDBOX: a deterministic scripted mini-run that drives TrajectorySession
/// exactly the way the future game adapter will. Produces a session directory
/// that must pass `sts2rec validate` and `sts2rec canonical` unchanged.
/// </summary>
public static class FakeGameScenario
{
    public const string Seed = "FAKESEED42";
    public const long StartTime = 1773034386;

    // Ground truth for cross-checks (verify_core.sh asserts canonical step count).
    public const int ExpectedStates = 4;   // 5 snapshots recorded, 1 deduped
    public const int ExpectedActions = 5;  // cancelled + executed plays, end_turn, map, shop
    public const int ExpectedEvents = 10;  // 5 prelude draws + 1 damage + 2 draws + 2 post-final
    public const int ExpectedCanonicalSteps = ExpectedActions;
    public const int ExpectedPreludeEvents = 5;

    public static SessionMeta Meta => new(
        RecorderVersion: "0.1.0",
        GameVersion: "0.108.0",
        GameCommit: "abc123def",
        BuildId: "20260701",
        Platform: "macos",
        Profile: "profile1",
        Seed: Seed,
        Character: "CHARACTER.DEFECT",
        Ascension: 1,
        GameMode: "standard",
        StartTime: StartTime,
        Untested: false,
        Mods: new[] { "Sts2Recorder" });

    /// <summary>Runs the scripted mini-run into outputRoot; returns the session directory.</summary>
    public static string Run(string outputRoot)
    {
        var clock = new FakeClock(StartTime, 0.25);
        using var session = TrajectorySession.Begin(outputRoot, Meta, clock);

        // --- combat starts: opening draws arrive BEFORE the first hooked action ---
        foreach (var card in new[]
                 { "CARD.STRIKE_DEFECT", "CARD.STRIKE_DEFECT", "CARD.DEFEND_DEFECT",
                   "CARD.ZAP", "CARD.DUALCAST" })
        {
            session.RecordEvent("card_drawn", new JsonObject { ["card"] = card });
        }

        // first stable snapshot; an immediate identical poll must dedupe
        var combat1 = CombatState(floor: 1, energy: 3, hand: 5);
        var s1 = session.RecordState("phase", "combat", combat1);
        var deduped = session.RecordState("poll", "combat", CombatState(floor: 1, energy: 3, hand: 5));
        Require(deduped == s1, "consecutive identical snapshot must dedupe to prior seq");

        // an enqueued play the player backs out of, then the real one
        session.RecordAction(
            "hook:ActionQueuePatch", "play_card",
            new JsonObject { ["card"] = "CARD.ZAP", ["target"] = 0 }, "cancelled", s1);
        session.RecordAction(
            "hook:ActionQueuePatch", "play_card",
            new JsonObject { ["card"] = "CARD.ZAP", ["target"] = 0 }, "executed", s1);
        session.RecordEvent(
            "damage_dealt", new JsonObject { ["amount"] = 8, ["target"] = "ENEMY.CULTIST" });

        var s2 = session.RecordState("action", "combat", CombatState(floor: 1, energy: 2, hand: 4));
        Require(s2 > s1, "post-action snapshot must advance seq");
        // end_turn resolves state_seq via LatestStateSeq (stateSeq: null)
        session.RecordAction("hook:PlayerCmdPatch", "end_turn", new JsonObject(), "executed", null);
        session.RecordEvent("card_drawn", new JsonObject { ["card"] = "CARD.DEFEND_DEFECT" });
        session.RecordEvent("card_drawn", new JsonObject { ["card"] = "CARD.STRIKE_DEFECT" });
        session.Flush(); // mid-run checkpoint, like the adapter's periodic flush

        // --- committed decisions: map node, then a shop purchase ---
        var s3 = session.RecordState(
            "phase", "map", new JsonObject { ["screen"] = "map", ["floor"] = 2 });
        session.RecordAction(
            "hook:MapPatch", "map_choice",
            new JsonObject { ["node"] = "x1y2", ["kind"] = "shop" }, "committed", s3);

        var s4 = session.RecordState(
            "phase", "shop",
            new JsonObject { ["screen"] = "shop", ["floor"] = 2, ["gold"] = 250 });
        session.RecordAction(
            "hook:ShopPatch", "shop_purchase",
            new JsonObject { ["item"] = "RELIC.ANCHOR", ["cost"] = 120 }, "committed", s4);

        // events after the FINAL action must still be captured (last-step bucket)
        session.RecordEvent("gold_changed", new JsonObject { ["delta"] = -120 });
        session.RecordEvent("relic_obtained", new JsonObject { ["relic"] = "RELIC.ANCHOR" });

        // --- run end: archive the game's native artifacts (copy, never move) ---
        var nativeSources = Path.Combine(outputRoot, "fake-native");
        Directory.CreateDirectory(nativeSources);
        var runFile = Path.Combine(nativeSources, $"{StartTime}.run");
        var mcrFile = Path.Combine(nativeSources, "latest.mcr");
        File.WriteAllText(runFile, "{\"schema\": 8, \"floor_reached\": 2}");
        File.WriteAllBytes(mcrFile, new byte[] { 0x4D, 0x43, 0x52, 0x00, 0x01 });
        session.ArchiveNative(runFile, "run_history.run");
        session.ArchiveNative(mcrFile, "replay.mcr");
        Require(File.Exists(runFile) && File.Exists(mcrFile), "ArchiveNative must copy, not move");

        session.Complete(new RunResult(Win: true, Abandoned: false, EndTime: clock.Now));
        return session.Directory;
    }

    // --- native anchors (R1 run saves, R2 replays, R11 unlock profile) ---

    public const string HiddenSeed = "SECRETSEED77";
    public const int NativeExpectedStates = 3;
    public const int NativeExpectedActions = 3;
    public const int NativeExpectedEvents = 5;   // unlock_profile, 2 run_saved, card_drawn, replay_archived
    public const int NativeExpectedSaves = 2;
    public const int NativeExpectedReplays = 2;  // win-path replay + post-Complete CleanUp replay

    /// <summary>Result of <see cref="RunWithNativeAnchors"/>.</summary>
    public sealed record NativeAnchorRun(
        string SessionDir,
        string GameSavePath,
        string GameReplayPath,
        IReadOnlyList<NativeArchiveReceipt> Receipts,
        IReadOnlyList<byte[]> SourceBytes);

    /// <summary>
    /// Scripted mini-run that drives the native-anchor writers the way the game
    /// adapter does: the unlock profile at run start, a copy of current_run.save
    /// after every SaveManager.Saved (room entry, then combat exit), a copy of
    /// latest.mcr after every WriteReplay, and the CleanUp-time replay that the
    /// game writes after the run already ended (no event, file only).
    /// </summary>
    public static NativeAnchorRun RunWithNativeAnchors(string outputRoot)
    {
        var clock = new FakeClock(StartTime, 0.25);
        var gameDir = Path.Combine(outputRoot, "fake-profile", "saves");
        Directory.CreateDirectory(gameDir);
        var savePath = Path.Combine(gameDir, "current_run.save");
        var replayPath = Path.Combine(gameDir, "latest.mcr");
        var receipts = new List<NativeArchiveReceipt>();
        var sources = new List<byte[]>();
        string sessionDir;
        using (var session = TrajectorySession.Begin(outputRoot, Meta, clock))
        {
            session.RecordEvent(
                NativeAnchorEvents.UnlockProfileEntry,
                NativeAnchorEvents.UnlockProfile(new UnlockProfileSnapshot(
                    new[] { "ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY" },
                    NumberOfRuns: 162, Wins: 40, Losses: 122, WongoPoints: 1500,
                    TestSubjectKills: 3, CharacterId: "CHARACTER.DEFECT", CharacterWins: 12)));

            var map = session.RecordState("phase", "map", new JsonObject { ["screen"] = "map", ["floor"] = 0 });

            sources.Add(GameWritesSave(savePath, preFinishedRoomType: null));
            receipts.Add(CaptureSave(session, savePath,
                new AnchorContext(0, 0, null, null, "Monster", 3, 1, session.LatestStateSeq)));
            session.RecordAction(
                "hook:NMapScreen.OnMapPointSelectedLocally", "vote_for_map_coord",
                new JsonObject { ["destination"] = new JsonObject { ["col"] = 3, ["row"] = 1 } }, "committed", map);

            var combat = session.RecordState("phase", "monster", CombatState(floor: 1, energy: 3, hand: 5));
            session.RecordEvent("card_drawn", new JsonObject { ["card"] = new JsonObject { ["id"] = "CARD.ZAP" } });
            session.RecordAction(
                "hook:ActionQueueSynchronizer.RequestEnqueue", "play_card",
                new JsonObject { ["card_model_id"] = "CARD.ZAP" }, "executed", combat);

            sources.Add(GameWritesSave(savePath, preFinishedRoomType: "monster"));
            receipts.Add(CaptureSave(session, savePath,
                new AnchorContext(0, 1, "Monster", "ENCOUNTER.NIBBITS_WEAK", "Monster", 3, 1, session.LatestStateSeq)));
            sources.Add(GameWritesReplay(replayPath, combatIndex: 1));
            receipts.Add(CaptureReplay(session, replayPath, duringCleanUp: false,
                new AnchorContext(0, 1, "Monster", "ENCOUNTER.NIBBITS_WEAK", "Monster", 3, 1, session.LatestStateSeq)));

            var rewards = session.RecordState("phase", "rewards", new JsonObject { ["screen"] = "rewards", ["floor"] = 1 });
            session.RecordAction(
                "hook:Reward.SelectUnsynchronized", "reward_taken",
                new JsonObject { ["reward_type"] = "gold_reward", ["amount"] = 15 }, "committed", rewards);

            session.Complete(new RunResult(Win: false, Abandoned: false, EndTime: clock.Now));

            sources.Add(GameWritesReplay(replayPath, combatIndex: 2));
            receipts.Add(CaptureReplay(session, replayPath, duringCleanUp: true, AnchorContext.Unknown));
            Require(session.WaitForNativeWrites(TimeSpan.FromSeconds(10)), "native writes must drain");
            sessionDir = session.Directory;
        }
        return new NativeAnchorRun(sessionDir, savePath, replayPath, receipts, sources);
    }

    /// <summary>The adapter's R1 path: read the finished save, summarize, archive.</summary>
    public static NativeArchiveReceipt CaptureSave(TrajectorySession session, string savePath, AnchorContext context)
    {
        var bytes = File.ReadAllBytes(savePath);
        var info = RunSaveInfo.Read(bytes);
        return session.ArchiveNativeSnapshot(
            NativeAnchorEvents.SavesCategory, NativeAnchorEvents.SaveExtension, NativeAnchorEvents.RunSavedEntry,
            bytes, NativeAnchorEvents.RunSaved(NativeAnchorEvents.TriggerSaved, info, context));
    }

    /// <summary>The adapter's R2 path: read the finished replay and archive it.</summary>
    public static NativeArchiveReceipt CaptureReplay(
        TrajectorySession session, string replayPath, bool duringCleanUp, AnchorContext context)
    {
        var bytes = File.ReadAllBytes(replayPath);
        return session.ArchiveNativeSnapshot(
            NativeAnchorEvents.ReplaysCategory, NativeAnchorEvents.ReplayExtension,
            NativeAnchorEvents.ReplayArchivedEntry, bytes,
            NativeAnchorEvents.ReplayArchived(stopRecording: true, duringCleanUp, context));
    }

    /// <summary>A schema-20-shaped current_run.save carrying hidden RNG state.</summary>
    public static byte[] SaveJson(string? preFinishedRoomType, int numReloads = 0, long startTime = StartTime)
    {
        var preFinished = preFinishedRoomType == null
            ? ""
            : $"\"pre_finished_room\":{{\"room_type\":\"{preFinishedRoomType}\",\"encounter_id\":\"ENCOUNTER.NIBBITS_WEAK\",\"is_pre_finished\":true}},";
        var json = "{\"schema_version\":20,\"acts\":[{\"id\":\"ACT.OVERGROWTH\",\"rooms\":{\"normal_encounters_visited\":1}}],"
            + "\"current_act_index\":0,\"events_seen\":[],"
            + preFinished
            + $"\"rng\":{{\"seed\":\"{HiddenSeed}\",\"rngs\":{{\"shuffle\":{{\"counter\":7}}}}}},"
            + "\"players\":[{\"character_id\":\"CHARACTER.DEFECT\",\"deck\":[{\"id\":\"CARD.ZAP\"}]}],"
            + $"\"start_time\":{startTime},\"num_reloads\":{numReloads},\"platform_type\":\"steam\"}}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] GameWritesSave(string savePath, string? preFinishedRoomType)
    {
        var bytes = SaveJson(preFinishedRoomType);
        File.WriteAllBytes(savePath, bytes);
        return bytes;
    }

    private static byte[] GameWritesReplay(string replayPath, int combatIndex)
    {
        var bytes = new byte[64 + combatIndex];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i * 31 + combatIndex) & 0xFF);
        }
        File.WriteAllBytes(replayPath, bytes);
        return bytes;
    }

    private static JsonNode CombatState(int floor, int energy, int hand) =>
        new JsonObject
        {
            ["screen"] = "combat",
            ["floor"] = floor,
            ["energy"] = energy,
            ["hand_size"] = hand,
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FakeGameScenario invariant broken: {message}");
        }
    }
}
