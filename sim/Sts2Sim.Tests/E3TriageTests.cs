using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

/// <summary>E3 triage regressions (sim/specs/diff-report-e3-farm-2026-11-02.md), each from a minimal repro.</summary>
public static class E3TriageTests
{
    private static RunState NewRun()
    {
        var player = new PlayerState(CombatLoop.PlayerCombatId, StartingDecks.IroncladCharacterId, 50, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0);
        player.PlayerRng = new PlayerRngSet(99UL);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, 0);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(run, player, run.Rng.UpFront);
        run.SharedRelicGrabBag = new RelicGrabBag(true);
        run.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(run), run.Rng.UpFront);
        run.Odds = new RunOddsSet(run.Rng.UnknownMapPoint);
        return run;
    }

    public static void Test_E3_ImportedPlayerTakesCombatIdZero()
    {
        // Combat/CombatState.cs:703-706, :726-731: the player is attached first to a fresh CombatState (_nextCreatureId 0),
        // so enemies start at 1 and monster Rng seeds (:721) add those ids. The importer used 1 (every save-loaded combat
        // diverged on enemies.combat_ids).
        Assert.Equal(CombatLoop.PlayerCombatId, RunImporter.ImportedPlayerCombatId);
        Assert.Equal(0, RunImporter.ImportedPlayerCombatId);
    }

    public static void Test_E3_WeakEncountersCarryTheirTags()
    {
        // Models.Encounters/*Weak.cs `Tags`; ActModel.AddWithoutRepeatingTags (Models/ActModel.cs:357-369) skips a first
        // regular encounter that shares a tag with the last weak one (farm seed LJGLSHFUAU: SEAPUNK_WEAK then
        // SEAPUNK_NORMAL in the sim, SEWER_CLAM_NORMAL in the game).
        var expected = new Dictionary<string, EncounterTag>
        {
            ["CORPSE_SLUGS_WEAK"] = EncounterTag.Slugs,
            ["FUZZY_WURM_CRAWLER_WEAK"] = EncounterTag.Crawler,
            ["NIBBITS_WEAK"] = EncounterTag.Nibbit,
            ["SEAPUNK_WEAK"] = EncounterTag.Seapunk,
            ["SHRINKER_BEETLE_WEAK"] = EncounterTag.Shrinker,
            ["SLIMES_WEAK"] = EncounterTag.Slimes
        };
        foreach (var (id, tag) in expected)
        {
            Assert.SequenceEqual(new[] { tag }, EncounterRegistry.Get(id).Tags, id);
        }
        Assert.True(EncounterRegistry.Get("SEAPUNK_WEAK").SharesTagsWith(EncounterRegistry.Get("SEAPUNK_NORMAL")));
    }

    public static void Test_E3_ObtainRemovesTheRelicFromBothGrabBagsAndStampsTheFloor()
    {
        // Commands/RelicCmd.cs:411-415, :422 (farm K4P3FWUZYN: a treasure LASTING_CANDY stayed in the sim's Uncommon bag).
        var rs = NewRun();
        var player = rs.Players[0];
        string id = player.RelicGrabBag!.GetDequeContents(RelicRarity.Uncommon)[0];
        Assert.True(player.RelicGrabBag.Contains(id));
        var relic = RelicCmd.Obtain(rs, id, player);
        Assert.False(player.RelicGrabBag.Contains(id), "the player's bag drops it");
        Assert.False(rs.SharedRelicGrabBag!.Contains(id), "the shared bag drops it");
        Assert.Equal(rs.TotalFloor, relic.FloorAddedToDeck);
    }

    public static void Test_E3_StartingRelicsAreAddedOnFloorOne()
    {
        // Entities.Players/Player.cs:1140-1152 PopulateStartingRelics.
        var sim = RunSim.NewRun(new RunConfig("E3START01", StartingDecks.IroncladCharacterId, 0));
        Assert.True(sim.State.Players[0].Relics.Count > 0);
        Assert.True(sim.State.Players[0].Relics.All(r => r.FloorAddedToDeck == 1));
    }

    public static void Test_E3_CombatEndHooksReceiveTheCombatRoom()
    {
        // Combat/CombatManager.cs:1652, :1708, :1781 pass (CombatRoom)runState.CurrentRoom; FISHING_ROD counts monster rooms
        // only (Models.Relics/FishingRod.cs) and never counted in the sim (farm 545XJP726S).
        var rs = NewRun();
        var player = rs.Players[0];
        var rod = (FishingRod)RelicCmd.Obtain(rs, FishingRod.RelicId, player);
        var room = new CombatRoom(EncounterRegistry.Get("SLIMES_WEAK"));
        rs.CurrentRooms.Add(room);
        var cs = room.StartCombat(rs);
        foreach (var monster in cs.Monsters.ToArray())
        {
            CreatureCmd.Kill(cs, monster);
        }
        CombatLoop.CheckWinCondition(cs);
        Assert.Equal(CombatOutcome.Victory, cs.Outcome);
        Assert.Equal(1, rod.CombatsSeen, "AfterCombatEnd saw the monster room");
    }

    public static void Test_E3_AutomationDisplayAmountIsProjected()
    {
        // Models.Powers/AutomationPower.cs:111 cardsLeft (farm 9AH885L2ES: six combats stopped as projection_gap).
        var power = PowerRegistry.Create("AUTOMATION_POWER");
        Assert.Equal(((AutomationPower)power).CardsLeft, PlayerView.DisplayAmount(power));
    }

    public static void Test_E3_SoldCardSlotsDoNotCompareTheSaleFlag()
    {
        var pre = JsonNode.Parse("""
            {"screen":"shop","shop":{"items":[{"category":"card","stocked":false,"on_sale":false},
             {"category":"card","stocked":true,"id":"SIC_EM","on_sale":true,"price":38}]}}
            """)!.AsObject();
        var oracle = G3Schema.NormalizeOracle(pre, Screens.Shop, out _);
        var entries = (JsonArray)oracle["shop"]!["entries"]!;
        Assert.False(((JsonObject)entries[0]!).ContainsKey("on_sale"), "a sold slot has no card to be on sale");
        Assert.True(((JsonObject)entries[1]!).ContainsKey("on_sale"));
    }

    public static void Test_E3_LaggingEventProceedIsSettledFromTheNextObservedState()
    {
        // JUNGLE_MAZE_ADVENTURE SOLO_QUEST (farm SFY7CMFJHB f6): the proceed pre_state still shows the option page and the
        // pre-option HP / gold; the next map state has the settled values the sim also has.
        var oracle = JsonNode.Parse("""
            {"screen":"event","player":{"hp":61,"max_hp":75,"gold":50},
             "event":{"id":"JUNGLE_MAZE_ADVENTURE","options":[{"key":"JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST","locked":false,"proceed":false}]}}
            """)!.AsObject();
        var sim = JsonNode.Parse("""
            {"screen":"event","player":{"hp":43,"max_hp":75,"gold":195},
             "event":{"id":"JUNGLE_MAZE_ADVENTURE","finished":true,"options":[{"key":"PROCEED","locked":false,"proceed":true}]}}
            """)!.AsObject();
        var next = JsonNode.Parse("""{"hp":43,"max_hp":75,"gold":195}""")!.AsObject();
        Assert.NotNull(G3Loop.LaggingEventProceed(oracle, sim, next));
        var wrongNext = JsonNode.Parse("""{"hp":40,"max_hp":75,"gold":195}""")!.AsObject();
        Assert.Null(G3Loop.LaggingEventProceed(oracle, sim, wrongNext));
    }
}
