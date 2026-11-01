using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class RunLoopTests
{
    private static RunState NewStandalone()
        => RunState.CreateStandalone(new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3), new RunRngSet(7UL), 0);

    public static void Test_RunLoop_RollRoomTypeForFixedPointTypes()
    {
        var rs = NewStandalone();
        var none = Array.Empty<RoomType>();
        Assert.Equal(RoomType.Shop, RunLoop.RollRoomTypeFor(rs, MapPointType.Shop, none));
        Assert.Equal(RoomType.Treasure, RunLoop.RollRoomTypeFor(rs, MapPointType.Treasure, none));
        Assert.Equal(RoomType.RestSite, RunLoop.RollRoomTypeFor(rs, MapPointType.RestSite, none));
        Assert.Equal(RoomType.Monster, RunLoop.RollRoomTypeFor(rs, MapPointType.Monster, none));
        Assert.Equal(RoomType.Elite, RunLoop.RollRoomTypeFor(rs, MapPointType.Elite, none));
        Assert.Equal(RoomType.Boss, RunLoop.RollRoomTypeFor(rs, MapPointType.Boss, none));
        Assert.Equal(RoomType.Event, RunLoop.RollRoomTypeFor(rs, MapPointType.Ancient, none), "an ancient point is an event room");
        Assert.Equal(RoomType.Unassigned, RunLoop.RollRoomTypeFor(rs, MapPointType.Unassigned, none), "veteran profile: no tutorial override");
    }

    public static void Test_RunLoop_TutorialOverrideOnlyForFirstRun()
    {
        var rs = NewStandalone();
        rs.Config = new RunConfig("SEED", StartingDecks.IroncladCharacterId, 0) { Progress = new RunProfileProgress(Array.Empty<string>(), 0, 0) };
        Assert.Equal(RoomType.Event, RunLoop.RollRoomTypeFor(rs, MapPointType.Unassigned, Array.Empty<RoomType>()), "first run: an unassigned point becomes an event");
        Assert.Equal(RoomType.Monster, RunLoop.RollRoomTypeFor(rs, MapPointType.Monster, Array.Empty<RoomType>()), "the override only touches unassigned points");
    }

    public static void Test_RunLoop_BlacklistAllShopChildren()
    {
        var a = new MapPoint(0, 1) { PointType = MapPointType.Shop };
        var b = new MapPoint(1, 1) { PointType = MapPointType.Shop };
        var c = new MapPoint(2, 1) { PointType = MapPointType.Monster };
        Assert.True(RunLoop.BuildRoomTypeBlacklist(null, new[] { a, b }).SetEquals(new[] { RoomType.Shop }), "every child is a shop");
        Assert.Equal(0, RunLoop.BuildRoomTypeBlacklist(null, new[] { a, c }).Count, "a non-shop child keeps shops allowed");
        Assert.Equal(0, RunLoop.BuildRoomTypeBlacklist(null, Array.Empty<MapPoint>()).Count, "no children, no blacklist");
    }

    public static void Test_RunLoop_BlacklistPreviousShopRoom()
    {
        W1Pending.Run("Test_RunLoop_BlacklistPreviousShopRoom", "R1", () =>
        {
            var previous = new MapPointHistoryEntry(MapPointType.Shop);
            previous.Rooms.Add(new MapPointRoomHistoryEntry(RoomType.Shop, null, Array.Empty<string>(), 0));
            Assert.True(RunLoop.BuildRoomTypeBlacklist(previous, Array.Empty<MapPoint>()).Contains(RoomType.Shop));
        });
    }

    public static void Test_RunLoop_AddVisitedMapCoordRejectsRepeats()
    {
        var rs = NewStandalone();
        rs.NextRoomId = 4;
        Assert.True(RunLoop.AddVisitedMapCoord(rs, new MapCoord(1, 0)));
        Assert.Equal(0, rs.NextRoomId, "entering a new coord resets the room id counter");
        rs.NextRoomId = 2;
        Assert.False(RunLoop.AddVisitedMapCoord(rs, new MapCoord(1, 0)), "a visited coord is rejected");
        Assert.Equal(2, rs.NextRoomId);
        Assert.Equal(new MapCoord(1, 0), RunLoop.CurrentMapCoord(rs));
    }

    public static void Test_RunLoop_ResolvePhaseFromRoomsAndLoopState()
    {
        var rs = NewStandalone();
        var loop = new RunLoopState();
        Assert.Equal(RunPhase.Map, RunLoop.ResolvePhase(rs, loop), "no room yet");
        rs.CurrentRooms.Add(new MerchantRoom());
        Assert.Equal(RunPhase.Shop, RunLoop.ResolvePhase(rs, loop));
        loop.MapOpen = true;
        Assert.Equal(RunPhase.Map, RunLoop.ResolvePhase(rs, loop), "the open map wins over the room");
        rs.RewardsStack.Add(new RewardsSet(rs.Players[0]));
        Assert.Equal(RunPhase.Rewards, RunLoop.ResolvePhase(rs, loop), "a rewards screen sits above the map");
        loop.OpenCardReward = 0;
        Assert.Equal(RunPhase.CardReward, RunLoop.ResolvePhase(rs, loop));
        rs.RewardsStack.Clear();
        loop.OpenCardReward = RunLoopState.NoCardReward;
        loop.MapOpen = false;
        rs.CurrentRooms[0] = new RestSiteRoom();
        Assert.Equal(RunPhase.RestSite, RunLoop.ResolvePhase(rs, loop));
        rs.CurrentRooms[0] = new TreasureRoom(0);
        Assert.Equal(RunPhase.Treasure, RunLoop.ResolvePhase(rs, loop));
        rs.Players[0].CurrentHp = 0;
        Assert.Equal(RunPhase.Defeat, RunLoop.ResolvePhase(rs, loop), "every player dead ends the run");
    }

    public static void Test_RunLoop_CombatPhaseListsCombatSimActions()
    {
        var sim = CombatSim.Create(11UL, StartingDecks.Ironclad, NibbitsWeak.EncounterIdEntry);
        var rs = sim.State.Run;
        var loop = new RunLoopState { Phase = RunLoop.ResolvePhase(rs, new RunLoopState()) };
        Assert.Equal(RunPhase.Combat, loop.Phase);
        var runActions = new List<RunAction>();
        RunLegalActionGenerator.Fill(rs, loop, null, Array.Empty<int>(), runActions);
        var expected = sim.LegalActions().Select(RunAction.FromCombat).ToList();
        Assert.SequenceEqual(expected, runActions, "combat phase = CombatSim.LegalActions mapped in the same order");
        Assert.Equal(RunActionKind.EndTurn, runActions[^1].Kind, "EndTurn last");
    }

    public static void Test_RunLoop_CombatStepMatchesCombatSim()
    {
        var reference = CombatSim.Create(23UL, StartingDecks.Ironclad, NibbitsWeak.EncounterIdEntry);
        var viaRun = CombatSim.Create(23UL, StartingDecks.Ironclad, NibbitsWeak.EncounterIdEntry);
        var rs = viaRun.State.Run;
        var loop = new RunLoopState { Phase = RunPhase.Combat };
        var policy = new System.Random(5);
        for (int i = 0; i < 6 && !reference.IsTerminal; i++)
        {
            var legal = reference.LegalActions();
            var action = legal[policy.Next(legal.Count)];
            reference.Step(action);
            RunActionExecutor.Execute(rs, loop, RunAction.FromCombat(action));
            if (reference.IsTerminal)
            {
                break;
            }
            Assert.Equal(RunPhase.Combat, loop.Phase);
            Assert.Equal(reference.StateHash(), StateHasher.Hash(rs.Combat!), $"step {i}: the run executor steps the combat exactly like CombatSim");
        }
    }

    public static void Test_RunLoop_OutOfCombatPotionLegality()
    {
        var rs = NewStandalone();
        var player = rs.Players[0];
        player.AddPotionInternal(PotionRegistry.Create(BloodPotion.PotionId), 0);
        player.AddPotionInternal(PotionRegistry.Create(FirePotion.PotionId), 1);
        Assert.True(RunLegalActionGenerator.CanUsePotionOutOfCombat(player, 0), "AnyTime potions are usable out of combat");
        Assert.False(RunLegalActionGenerator.CanUsePotionOutOfCombat(player, 1), "CombatOnly potions are not");
        Assert.False(RunLegalActionGenerator.CanUsePotionOutOfCombat(player, 2), "empty slot");
        Assert.True(RunLegalActionGenerator.CanDiscardPotionOutOfCombat(player, 1));
        rs.CurrentRooms.Add(new RestSiteRoom());
        var loop = new RunLoopState { Phase = RunPhase.RestSite };
        var actions = new List<RunAction>();
        RunLegalActionGenerator.Fill(rs, loop, null, Array.Empty<int>(), actions);
        Assert.SequenceEqual(new[]
        {
            RunAction.Of(RunActionKind.UsePotion, 0, RunAction.NoIndex),
            RunAction.Of(RunActionKind.DiscardPotion, 0),
            RunAction.Of(RunActionKind.DiscardPotion, 1),
            RunAction.Of(RunActionKind.LeaveRestSite)
        }, actions, "potions after the phase actions, leave last");
        player.CanUseOrRemovePotions = false;
        RunLegalActionGenerator.Fill(rs, loop, null, Array.Empty<int>(), actions);
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.LeaveRestSite) }, actions, "CanUseOrRemovePotions false disables use and discard");
    }

    public static void Test_RunLoop_TreasureChestMustOpenFirst()
    {
        var rs = NewStandalone();
        rs.CurrentRooms.Add(new TreasureRoom(0));
        var loop = new RunLoopState { Phase = RunPhase.Treasure };
        var actions = new List<RunAction>();
        RunLegalActionGenerator.Fill(rs, loop, null, Array.Empty<int>(), actions);
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.OpenChest) }, actions);
    }

    public static void Test_RunSim_NewRunStartsAtNeowAndClonesHashEqual()
    {
        W1Pending.Run("Test_RunSim_NewRunStartsAtNeowAndClonesHashEqual", "R1", () =>
        {
            var sim = RunSim.NewRun(new RunConfig("RUNLOOP1", StartingDecks.IroncladCharacterId, 0));
            Assert.Equal(RunPhase.Event, sim.Phase, "a veteran profile starts on the Neow point");
            Assert.True(sim.LegalActions().Count > 0);
            var clone = sim.Clone();
            Assert.Equal(sim.StateHash(), clone.StateHash());
            var first = sim.LegalActions()[0];
            clone.Step(first);
            Assert.NotEqual(sim.StateHash(), clone.StateHash(), "clones are independent");
        });
    }

    public static void Test_RunSoak_RandomRuns()
    {
        W1Pending.Run("Test_RunSoak_RandomRuns", "R1", () =>
        {
            var result = RunSoak.Run(new RunSoakOptions { Runs = 10_000 });
            if (result.FailureCount > 0)
            {
                Assert.Fail($"{result.FailureCount} of {result.Runs} runs failed:\n" + string.Join("\n", result.Failures));
            }
            Assert.Equal(result.Runs, result.Victories + result.Defeats + result.Capped);
        });
    }
}
