using System;
using System.IO;
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
