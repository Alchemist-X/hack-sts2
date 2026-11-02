using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
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
        var previous = new MapPointHistoryEntry(MapPointType.Shop);
        previous.Rooms.Add(new MapPointRoomHistoryEntry(RoomType.Shop, null, Array.Empty<string>(), 0));
        Assert.True(RunLoop.BuildRoomTypeBlacklist(previous, Array.Empty<MapPoint>()).Contains(RoomType.Shop));
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
        var sim = RunSim.NewRun(new RunConfig("RUNLOOP1", StartingDecks.IroncladCharacterId, 0));
        Assert.Equal(RunPhase.Event, sim.Phase, "a veteran profile starts on the Neow point");
        var room = Assert.NotNull(RoomStack.CurrentRoom(sim.State) as EventRoom);
        Assert.Equal("NEOW", room.CanonicalEvent.Id);
        Assert.Equal(0, room.Id, "the first room of a map point takes id 0");
        Assert.Equal(1, sim.State.ActFloor);
        Assert.True(sim.LegalActions().Count > 0);
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        var first = sim.LegalActions()[0];
        clone.Step(first);
        Assert.NotEqual(sim.StateHash(), clone.StateHash(), "clones are independent");
    }

    public static void Test_RunSoak_RandomRuns()
    {
        var result = RunSoak.Run(new RunSoakOptions { Runs = 10_000, CharacterIds = CharacterRegistry.AllIds });
        if (result.FailureCount > 0)
        {
            Assert.Fail($"{result.FailureCount} of {result.Runs} runs failed:\n" + string.Join("\n", result.Failures));
        }
        Assert.Equal(result.Runs, result.Victories + result.Defeats + result.Capped + result.ContentGapCount);
        Console.WriteLine($"     RunSoak: {result.Runs} runs x {CharacterRegistry.AllIds.Count} characters, {result.Steps} steps, {result.Victories} victories, {result.Defeats} defeats, {result.Capped} capped, {result.ContentGapCount} stopped on a content/kernel stub");
        foreach (var gap in result.ContentGaps.OrderByDescending(g => g.Value))
        {
            Console.WriteLine($"     RunSoak content gap x{gap.Value}: {gap.Key}");
        }
    }

    public static void Test_RunLoop_FullRunsReachTheArchitect()
    {
        int victories = 0;
        var buffer = new List<RunAction>();
        for (int seed = 1; seed <= 12; seed++)
        {
            var sim = RunSim.NewRun(new RunConfig(RunSoak.SeedText(seed), StartingDecks.IroncladCharacterId, 0) { Content = ContentFilter.PortedOnly });
            var player = sim.State.Players[0];
            player.MaxHp = 1_000_000;
            player.CurrentHp = 1_000_000;
            var random = new System.Random(seed);
            int maxAct = 0;
            try
            {
                for (int n = 0; n < 60_000 && !sim.IsTerminal; n++)
                {
                    sim.LegalActions(buffer);
                    Assert.True(buffer.Count > 0, $"seed {seed}: no legal action in {sim.Phase}");
                    var action = buffer[random.Next(buffer.Count)];
                    if (sim.Phase == RunPhase.Combat && action.Kind == RunActionKind.EndTurn && buffer.Count > 1 && random.Next(4) != 0)
                    {
                        action = buffer[random.Next(buffer.Count)];
                    }
                    sim.Step(action);
                    maxAct = Math.Max(maxAct, sim.State.CurrentActIndex);
                }
            }
            catch (NotImplementedException ex) when (!RunSoak.IsRunLayerStub(ex))
            {
                continue;
            }
            if (sim.Phase == RunPhase.Victory)
            {
                victories++;
                Assert.Equal(sim.State.Acts.Count - 1, maxAct);
                Assert.True(RoomStack.CurrentRoom(sim.State)!.IsVictoryRoom);
                Assert.Equal("THE_ARCHITECT", RoomStack.CurrentRoom(sim.State)!.ModelId);
                Assert.Equal(sim.StateHash(), sim.Clone().StateHash());
            }
        }
        Assert.True(victories > 0, "no high-HP random run reached the Architect");
    }

    public static void Test_RunLoop_EventCombatVictoryResumesTheEvent()
    {
        var rs = RunLoop.CreateForNewRun(new RunConfig("EVCOMBAT", StartingDecks.IroncladCharacterId, 0));
        RunLoop.EnterAct(rs, 0);
        RoomStack.EnterRoom(rs, new EventRoom(new CombatEventDouble()));
        var loop = new RunLoopState();
        RunLoop.AfterAction(rs, loop);
        Assert.Equal(RunPhase.Event, loop.Phase);
        var ev = Assert.NotNull(RunLegalActionGenerator.CurrentEvent(rs));
        ev.ChooseOption(rs, 0);
        RunLoop.AfterAction(rs, loop);
        Assert.Equal(RunPhase.Combat, loop.Phase);
        Assert.Equal(2, rs.CurrentRooms.Count);
        var cs = Assert.NotNull(rs.Combat);
        var player = rs.Players[0];
        player.CurrentHp -= 7;
        int hp = player.CurrentHp;
        CreatureCmd.Kill(cs, cs.Monsters.Cast<CreatureState>().ToList());
        CombatLoop.CheckWinCondition(cs);
        RunLoop.AfterAction(rs, loop);
        var room = Assert.NotNull(RoomStack.CurrentRoom(rs) as CombatRoom);
        Assert.True(room.IsPreFinished, "victory marks the combat room pre-finished");
        Assert.Null(rs.Combat);
        Assert.True(ReferenceEquals(player, cs.Player), "the combat fights with the run's player");
        Assert.Equal(hp + 6, rs.Players[0].CurrentHp, "combat HP stays on the run player (BURNING_BLOOD heals 6 at combat end)");
        Assert.Equal(RunPhase.Rewards, loop.Phase);
        Assert.True(RunLegalActionGenerator.TopRewards(rs)!.IsTerminal, "room-end rewards of a combat room are terminal");
        RunActionExecutor.Execute(rs, loop, RunAction.Of(RunActionKind.ProceedRewards));
        Assert.Equal(1, rs.CurrentRooms.Count);
        Assert.True(RoomStack.CurrentRoom(rs) is EventRoom);
        Assert.Equal(RunPhase.Event, loop.Phase, "the parent event resumes after the terminal rewards screen");
        Assert.Equal(1, ((CombatEventDouble)ev).Resumed);
    }

    public static void Test_RunLoop_CardRemovalRewardPromptsTheDeck()
    {
        var sim = RunSim.NewRun(new RunConfig("REMOVAL1", StartingDecks.IroncladCharacterId, 0));
        var rs = sim.State;
        var player = rs.Players[0];
        int deck = player.Deck.Count;
        var reward = new CardRemovalReward(player);
        rs.RewardsStack.Add(new RewardsSet(player).WithCustomRewards(new List<Reward> { reward }));
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, 0));
        var prompt = Assert.NotNull(sim.PendingChoice);
        Assert.Equal(ChoicePromptKind.DeckRemove, prompt.Kind);
        Assert.True(prompt.Cancelable);
        Assert.Equal(deck, prompt.Options.Count);
        sim.Step(RunAction.Of(RunActionKind.PromptSelect, 0));
        sim.Step(RunAction.Of(RunActionKind.PromptConfirm));
        Assert.False(sim.HasPendingChoice);
        Assert.Equal(deck - 1, sim.State.Players[0].Deck.Count);
        Assert.True(sim.State.RewardsStack[^1].Rewards[0].SuccessfullySelected);
    }

    [SimExclude]
    private sealed class CombatEventDouble : EventModel
    {
        public int Resumed { get; private set; }

        public override string Id => "COMBAT_EVENT_DOUBLE";

        public override bool IsShared => true;

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
            => new[] { new EventOption(InitialOptionKey("FIGHT"), Fight) };

        private void Fight(RunState rs)
            => EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(NibbitsWeak.EncounterIdEntry), Array.Empty<Reward>(), shouldResumeAfterCombat: true);

        public override void Resume(RunState rs, AbstractRoom exitedRoom) => Resumed++;
    }
}
