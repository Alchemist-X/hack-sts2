using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class TurnFlowTests
{
    private const string Encounter = "TURN_FLOW_TEST_ENCOUNTER";
    private const string Strike = StrikeIronclad.CardId;
    private const string Defend = DefendIronclad.CardId;
    private const string CombatEndedAtPauseSite = "CombatLoop.StartTurn:combat-ended-at-turn-setup-pause";
    private const string GeneratedOutsideCombatSite = "CardPileCmd.AddGeneratedCardToCombat:empty-result-outside-running-combat";

    public static void Test_TurnFlow_DeckCardsPrecedeTheirCombatCopies()
    {
        var deck = StartingDecks.Ironclad.ToArray();
        var sim = Create(5UL, deck, new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        Assert.Equal(deck.Length, p.Deck.Count, "the deck holds one card per spec");
        for (int i = 0; i < deck.Length; i++)
        {
            var card = p.Deck[i];
            Assert.Equal(i, card.InstanceId, "deck cards take the first ids, in deck order");
            Assert.Equal(deck[i], card.Id);
            Assert.True(card.HasOwner && ReferenceEquals(card.Owner, p), "deck cards are attached to the player");
            Assert.Equal(CardInstance.NoDeckVersion, card.DeckVersionId);
        }
        var combat = CombatCards(p);
        Assert.Equal(deck.Length, combat.Count, "every deck card has one combat copy");
        var seen = new HashSet<int>();
        foreach (var copy in combat)
        {
            Assert.True(seen.Add(copy.DeckVersionId), $"copy {copy.Id}#{copy.InstanceId} links a deck card no other copy links");
            Assert.Equal(deck.Length + copy.DeckVersionId, copy.InstanceId, "copies are allocated in deck order, after the deck");
            var source = p.Deck[copy.DeckVersionId];
            Assert.False(ReferenceEquals(source, copy), "a combat copy is its own object");
            Assert.Equal(source.Id, copy.Id);
            Assert.Equal(source.UpgradeLevel, copy.UpgradeLevel);
            Assert.True(copy.HasOwner && ReferenceEquals(copy.Owner, p), "copies keep the deck card's owner");
            Assert.False(copy.HasBeenRemovedFromState);
        }
        Assert.Equal(2 * deck.Length, cs.NextCardInstanceId);
        Assert.Equal(cs.NextCardInstanceId, cs.Run.NextCardInstanceId, "the combat forwards to the run allocator");

        var handCard = p.Hand[0];
        var deckCard = p.Deck[handCard.DeckVersionId];
        handCard.Upgrade();
        Assert.True(handCard.IsUpgraded);
        Assert.False(deckCard.IsUpgraded, "an in-combat upgrade leaves the deck card unchanged");
        Assert.Equal(0, deckCard.UpgradeLevel);

        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        var clonedPlayer = clone.State.Player;
        for (int i = 0; i < deck.Length; i++)
        {
            Assert.False(ReferenceEquals(clonedPlayer.Deck[i], p.Deck[i]), "the clone copies the deck");
            Assert.True(ReferenceEquals(clonedPlayer.Deck[i].Owner, clonedPlayer), "cloned deck cards belong to the cloned player");
        }

        var specs = CardSpec.FromIds(deck);
        specs[9] = specs[9] with { Upgraded = true };
        var upgraded = CombatLoop.CreateCombat(new RunRngSet(5UL), specs, Encounter, 0, null, null, Spawn(new Dummy("TURN_FLOW_DUMMY_A", 60)));
        Assert.True(upgraded.Player.Deck[9].IsUpgraded, "an upgraded spec gives an upgraded deck card");
        var upgradedCopy = CombatCards(upgraded.Player).Single(c => c.DeckVersionId == 9);
        Assert.True(upgradedCopy.IsUpgraded, "the combat copy carries the deck card's upgrade");
        Assert.Equal(1, CombatCards(upgraded.Player).Count(c => c.IsUpgraded));
    }

    public static void Test_TurnFlow_SideTurnHooksReceiveTheGamesParticipantSnapshots()
    {
        var log = new List<string>();
        var sim = Create(7UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60, bite: 2));
        var cs = sim.State;
        var p = cs.Player;
        var first = cs.Monsters[0];
        var probe = AttachProbe(cs, p, log);
        MonsterState? spawned = null;
        probe.OnEvent = (state, tag) =>
        {
            if (tag == "BeforeSideTurnStart:Enemy" && spawned == null)
            {
                var def = new Dummy("TURN_FLOW_DUMMY_B", 20);
                spawned = new MonsterState(state.AllocateCreatureId(), def, 20, 20, null, null);
                state.AddMonster(spawned);
                MonsterLifecycle.SetUpForCombat(state, spawned);
            }
            else if (tag == "BeforeSideTurnEnd:Enemy" && spawned is { IsAlive: true })
            {
                CreatureCmd.Kill(state, spawned);
            }
        };
        probe.Sides.Clear();
        sim.Step(GameAction.EndTurn);
        var b = Assert.NotNull(spawned);
        var sides = probe.Sides;
        Assert.SequenceEqual(
            new[]
            {
                "BeforeSideTurnEnd:Player", "AfterSideTurnEnd:Player",
                "BeforeSideTurnStart:Enemy", "AfterSideTurnStart:Enemy",
                "BeforeSideTurnEnd:Enemy", "AfterSideTurnEnd:Enemy",
                "BeforeSideTurnStart:Player", "AfterSideTurnStart:Player"
            },
            sides.Select(s => s.Hook));
        Assert.SequenceEqual(new[] { p.CombatId }, Ids(sides[0].Participants), "the player turn end passes the ending players");
        Assert.SequenceEqual(new[] { p.CombatId }, Ids(sides[1].Participants));
        Assert.SequenceEqual(new[] { first.CombatId }, Ids(sides[2].Participants), "the enemy turn start snapshot is taken before the spawn");
        Assert.True(ReferenceEquals(sides[2].Participants, sides[3].Participants), "BeforeSideTurnStart and AfterSideTurnStart share one snapshot");
        Assert.SequenceEqual(new[] { first.CombatId }, Ids(sides[3].Participants), "a creature spawned meanwhile is not a participant");
        Assert.Equal(2, sides[3].LiveMonsters, "the spawned creature is in the live list");
        Assert.SequenceEqual(new[] { first.CombatId, b.CombatId }, Ids(sides[4].Participants), "the enemy turn end snapshot holds both enemies");
        Assert.True(ReferenceEquals(sides[4].Participants, sides[5].Participants), "BeforeSideTurnEnd and AfterSideTurnEnd share one snapshot");
        Assert.SequenceEqual(new[] { first.CombatId, b.CombatId }, Ids(sides[5].Participants), "a creature that died meanwhile stays a participant");
        Assert.Equal(1, sides[5].LiveMonsters, "the killed creature left the live list");
        Assert.SequenceEqual(new[] { p.CombatId }, Ids(sides[6].Participants), "the player turn start passes the player-side creatures");
        Assert.True(ReferenceEquals(sides[6].Participants, sides[7].Participants));
        foreach (var side in sides)
        {
            Assert.False(ReferenceEquals(side.Participants, cs.Monsters), $"{side.Hook} gets a snapshot, not the live enemy list");
            Assert.False(ReferenceEquals(side.Participants, cs.Allies), $"{side.Hook} gets a snapshot, not the live ally list");
        }

        W1Pending.Run("TurnFlowTests.Test_TurnFlow_SideTurnHooksReceiveTheGamesParticipantSnapshots (pet)", "state", () =>
        {
            var petLog = new List<string>();
            var petSim = Create(7UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
            var pcs = petSim.State;
            var owner = pcs.Player;
            var pet = new MonsterState(pcs.AllocateCreatureId(), new Dummy("TURN_FLOW_PET", 10), 10, 10, null, null)
            {
                Side = CombatSide.Player,
                PetOwnerId = owner.CombatId
            };
            pcs.AddAlly(pet);
            owner.PetIds.Add(pet.CombatId);
            var petProbe = AttachProbe(pcs, owner, petLog);
            petSim.Step(GameAction.EndTurn);
            var petSides = petProbe.Sides;
            var playerEnd = petSides.First(s => s.Hook == "BeforeSideTurnEnd:Player");
            Assert.SequenceEqual(new[] { owner.CombatId }, Ids(playerEnd.Participants), "pets do not end the turn with the player");
            var playerStart = petSides.Last(s => s.Hook == "BeforeSideTurnStart:Player");
            Assert.SequenceEqual(new[] { owner.CombatId, pet.CombatId }, Ids(playerStart.Participants), "pets start the turn with the player side");
        });
    }

    public static void Test_TurnFlow_EffectDepthGatesTheEmptyHandCheck()
    {
        var log = new List<string>();
        var sim = Create(11UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        AttachProbe(cs, p, log);
        var depths = new List<int>();
        var sly = cs.CreateCard(new ProbeCard("TURN_FLOW_SLY_SKILL", CardType.Skill, TargetType.Self, 1, new[] { CardKeyword.Sly }, (state, play) =>
        {
            depths.Add(state.CardOrPotionEffectDepth);
            log.Add("OnPlay:sly");
        }), p);
        var dump = cs.CreateCard(new ProbeCard("TURN_FLOW_DUMP_HAND", CardType.Skill, TargetType.Self, 0, Array.Empty<CardKeyword>(), (state, play) =>
        {
            depths.Add(state.CardOrPotionEffectDepth);
            var rest = state.Player.Hand.Cards.Where(c => !ReferenceEquals(c, play.Card)).ToList();
            CardCmd.Discard(state, rest);
            log.Add("OnPlay:dump-done");
        }), p);
        CardPileCmd.AddGeneratedCardToCombat(cs, sly, PileType.Hand);
        CardPileCmd.AddGeneratedCardToCombat(cs, dump, PileType.Hand);
        Assert.Equal(7, p.Hand.Count);
        log.Clear();
        sim.Step(GameAction.Play(p.Hand.IndexOf(dump)));
        Assert.SequenceEqual(new[] { 1, 2 }, depths, "the played card runs at depth 1; the Sly auto-play inside its discard runs at depth 2");
        var emptied = log.Where(t => t.StartsWith("AfterHandEmptied", StringComparison.Ordinal)).ToList();
        Assert.SequenceEqual(new[] { "AfterHandEmptied:depth0" }, emptied, "only the outer play checks the empty hand, after its effect ended");
        Assert.True(log.IndexOf("OnPlay:dump-done") < log.IndexOf("AfterHandEmptied:depth0"));
        Assert.True(log.IndexOf("OnPlay:sly") < log.IndexOf("OnPlay:dump-done"), "the Sly card is auto-played inside the discard");
        Assert.Equal(0, cs.CardOrPotionEffectDepth);
        Assert.Equal(0, p.Hand.Count);
        Assert.Equal(7, p.DiscardPile.Count);
        Assert.Equal(3, p.Energy, "the 0-cost play spends nothing and the auto-play spends nothing");
    }

    public static void Test_TurnFlow_TurnSetupFirstPauseRunsTheDeferredStartSteps()
    {
        var log = new List<string>();
        var sim = Create(13UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        var kinds = new List<ChoiceContextKind>();
        probe.OnEvent = (state, tag) =>
        {
            if (tag == "BeforeHandDraw:t2")
            {
                var frames = state.Choice.Contexts;
                var top = frames[frames.Count - 1];
                kinds.Add(top.Kind);
                log.Add("pause");
                Assert.NotNull(top.OnFirstPause).Invoke();
                top.OnFirstPause!.Invoke();
                log.Add("resume");
            }
        };
        log.Clear();
        sim.Step(GameAction.EndTurn);
        Assert.SequenceEqual(new[] { ChoiceContextKind.TurnSetup }, kinds, "SetupPlayerTurn runs in the turn-setup context");
        var turn = TurnStartSlice(log);
        Assert.SequenceEqual(
            new[]
            {
                "BeforeSideTurnStart:Player",
                "BeforeHandDraw:t2",
                "pause",
                "AfterSideTurnStart:Player",
                "resume",
                "AfterPlayerTurnStart:t2",
                "AfterAutoPrePlayPhaseEnteredEarly:AutoPrePlay",
                "AfterAutoPrePlayPhaseEntered:AutoPrePlay",
                "AfterAutoPrePlayPhaseEnteredLate:AutoPrePlay"
            },
            turn.Where(IsStartStep));
        Assert.Equal(1, turn.Count(t => t == "AfterSideTurnStart:Player"), "the deferred step is not repeated after the setup");
        Assert.Equal(PlayerTurnPhase.Play, p.Phase);
        Assert.Equal(5, p.Hand.Count);
        Assert.Equal(0, cs.Choice.ContextDepth, "every pushed context is popped");
        Assert.Equal(0, cs.Run.Fidelity.Count);
    }

    public static void Test_TurnFlow_FirstPauseInTheAutoPrePlayPhaseRunsOnlyTheWinCheck()
    {
        var log = new List<string>();
        var sim = Create(17UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var probe = AttachProbe(cs, cs.Player, log);
        probe.OnEvent = (state, tag) =>
        {
            if (tag == "AfterAutoPrePlayPhaseEntered:AutoPrePlay" && state.Player.TurnNumber == 2)
            {
                var frames = state.Choice.Contexts;
                Assert.Equal(ChoiceContextKind.TurnSetup, frames[frames.Count - 1].Kind);
                log.Add("pause");
                frames[frames.Count - 1].OnFirstPause!.Invoke();
                log.Add("resume");
            }
        };
        log.Clear();
        sim.Step(GameAction.EndTurn);
        var turn = TurnStartSlice(log);
        Assert.Equal(1, turn.Count(t => t == "AfterSideTurnStart:Player"));
        Assert.True(turn.IndexOf("AfterSideTurnStart:Player") < turn.IndexOf("pause"), "the side-turn start ran before the pre-play phase");
        Assert.True(turn.IndexOf("resume") < turn.IndexOf("AfterAutoPrePlayPhaseEnteredLate:AutoPrePlay"));
        Assert.Equal(PlayerTurnPhase.Play, cs.Player.Phase);
        Assert.Equal(CombatOutcome.InProgress, sim.Outcome);

        var endLog = new List<string>();
        var ending = Create(17UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var ecs = ending.State;
        var endProbe = AttachProbe(ecs, ecs.Player, endLog);
        endProbe.OnEvent = (state, tag) =>
        {
            if (tag == "AfterAutoPrePlayPhaseEntered:AutoPrePlay" && state.Player.TurnNumber == 2)
            {
                CreatureCmd.Kill(state, state.Monsters[0]);
                var frames = state.Choice.Contexts;
                frames[frames.Count - 1].OnFirstPause!.Invoke();
                endLog.Add("not-reached");
            }
        };
        ending.Step(GameAction.EndTurn);
        Assert.Equal(CombatOutcome.Victory, ending.Outcome, "the deferred win check ends the combat");
        Assert.True(ending.IsTerminal);
        Assert.False(endLog.Contains("not-reached"), "the paused phase is abandoned");
        Assert.False(endLog.Contains("AfterAutoPrePlayPhaseEnteredLate:AutoPrePlay"), "the late pass never runs");
        Assert.Equal(PlayerTurnPhase.None, ecs.Player.Phase, "the phase is not moved to Play after the combat ended");
        Assert.True(ecs.Run.Fidelity.Entries.Any(e => e.Flag == FidelityFlag.ApproximatedRule && e.Site == CombatEndedAtPauseSite), "the unwind is flagged");
        Assert.Equal(0, ecs.Choice.ContextDepth);
    }

    public static void Test_TurnFlow_ACombatEndAtTheDeferredWinCheckUnwindsTheSetup()
    {
        var log = new List<string>();
        var sim = Create(19UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var probe = AttachProbe(cs, cs.Player, log);
        probe.OnEvent = (state, tag) =>
        {
            if (tag == "BeforeHandDraw:t2")
            {
                var frames = state.Choice.Contexts;
                frames[frames.Count - 1].OnFirstPause!.Invoke();
                log.Add("not-reached");
            }
            else if (tag == "AfterSideTurnStart:Player" && state.Player.TurnNumber == 2)
            {
                CreatureCmd.Kill(state, state.Monsters[0]);
            }
        };
        log.Clear();
        sim.Step(GameAction.EndTurn);
        Assert.Equal(CombatOutcome.Victory, sim.Outcome, "the deferred win check ends the combat");
        Assert.True(sim.IsTerminal);
        Assert.False(log.Contains("not-reached"), "the paused setup is abandoned");
        var tail = log.Skip(log.LastIndexOf("BeforeSideTurnStart:Player")).ToList();
        Assert.SequenceEqual(new[] { "BeforeSideTurnStart:Player", "BeforeHandDraw:t2", "AfterSideTurnStart:Player", "AfterCombatEnd" }, tail, "the side-turn start runs at the pause, then the win check ends the combat");
        Assert.False(log.Contains("AfterPlayerTurnStart:t2"), "the setup after the pause is abandoned");
        Assert.Equal(0, cs.Player.Hand.Count, "the hand draw after the pause never ran");
        Assert.True(cs.Run.Fidelity.Entries.Any(e => e.Flag == FidelityFlag.ApproximatedRule && e.Site == CombatEndedAtPauseSite));
        Assert.Equal(0, cs.Choice.ContextDepth);
        Assert.Equal(0, sim.LegalActions().Count);
    }

    public static void Test_TurnFlow_TurnSetupPromptPausesThroughTheChoiceRuntime()
    {
        W1Pending.Run("TurnFlowTests.Test_TurnFlow_TurnSetupPromptPausesThroughTheChoiceRuntime", "choice", () =>
        {
            var log = new List<string>();
            var sim = Create(23UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
            var cs = sim.State;
            var p = cs.Player;
            var probe = AttachProbe(cs, p, log);
            var options = new[]
            {
                cs.CreateCard(CardRegistry.Get(Strike), p),
                cs.CreateCard(CardRegistry.Get(Defend), p)
            };
            probe.OnEvent = (state, tag) =>
            {
                if (tag == "AfterPlayerTurnStart:t2")
                {
                    log.Add("prompt");
                    var picked = CardSelectCmd.FromSimpleGrid(state, options, state.Player, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1), ChoiceSource.For("TURN_FLOW_PROBE_POWER", ChoiceSource.NoInstance, "AfterPlayerTurnStart")).ToList();
                    log.Add("picked:" + string.Join(",", picked.Select(c => c.Id)));
                }
            };
            cs.Choice.Answerer = new LoggingAnswerer(log, 0);
            log.Clear();
            CombatLoop.Step(cs, GameAction.EndTurn);
            var turn = TurnStartSlice(log);
            Assert.True(turn.IndexOf("prompt") < turn.IndexOf("AfterSideTurnStart:Player"), "the side-turn start waits for the setup's first pause");
            Assert.True(turn.IndexOf("AfterSideTurnStart:Player") < turn.IndexOf("answer"), "the deferred steps run before the answer");
            Assert.Equal(1, turn.Count(t => t == "AfterSideTurnStart:Player"));
            Assert.True(turn.Contains("picked:" + Strike));
            Assert.Equal(PlayerTurnPhase.Play, p.Phase);
        });
    }

    public static void Test_TurnFlow_ExtraTurnKeepsThePlayerSideAndSkipsTheEnemyTurn()
    {
        var log = new List<string>();
        var moves = new List<string>();
        var sim = Create(29UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60, bite: 3, log: moves));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        int extraTurns = 1;
        probe.ExtraTurn = () => extraTurns > 0;
        probe.OnEvent = (state, tag) =>
        {
            if (tag.StartsWith("AfterTakingExtraTurn", StringComparison.Ordinal))
            {
                extraTurns--;
            }
        };
        int aiDraws = cs.Rng.MonsterAi.Counter;
        log.Clear();
        probe.Sides.Clear();
        sim.Step(GameAction.EndTurn);
        Assert.Equal(CombatSide.Player, cs.CurrentSide);
        Assert.Equal(1, cs.RoundNumber, "an extra turn does not start a round");
        Assert.Equal(2, p.TurnNumber);
        Assert.SequenceEqual(new[] { p.CombatId }, cs.PlayersTakingExtraTurn, "the extra-turn players are part of the state during the extra turn");
        Assert.Equal(0, moves.Count, "the enemy does not act");
        Assert.Equal(aiDraws, cs.Rng.MonsterAi.Counter, "monsters do not prepare a move for an extra turn");
        Assert.Equal(80, p.CurrentHp);
        Assert.Equal(1, log.Count(t => t == "AfterTakingExtraTurn:" + p.CombatId));
        Assert.True(log.IndexOf("AfterSideTurnEnd:Player") < log.IndexOf("AfterTakingExtraTurn:" + p.CombatId));
        Assert.True(log.IndexOf("AfterTakingExtraTurn:" + p.CombatId) < log.IndexOf("BeforeSideTurnStart:Player"));
        Assert.False(log.Contains("BeforeSideTurnStart:Enemy"));
        var start = probe.Sides.First(s => s.Hook == "BeforeSideTurnStart:Player");
        Assert.SequenceEqual(new[] { p.CombatId }, Ids(start.Participants), "the extra-turn players start the turn");
        Assert.Equal(PlayerTurnPhase.Play, p.Phase);
        Assert.Equal(3, p.Energy);
        Assert.Equal(5, p.Hand.Count);

        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        Assert.SequenceEqual(cs.PlayersTakingExtraTurn, clone.State.PlayersTakingExtraTurn);
        clone.State.PlayersTakingExtraTurn.Clear();
        Assert.NotEqual(sim.StateHash(), clone.StateHash(), "the extra-turn list is hashed");

        sim.Step(GameAction.EndTurn);
        Assert.Equal(0, cs.PlayersTakingExtraTurn.Count, "the switch clears the list when nobody takes another extra turn");
        Assert.SequenceEqual(new[] { "TURN_FLOW_DUMMY_A" }, moves);
        Assert.Equal(2, cs.RoundNumber);
        Assert.Equal(3, p.TurnNumber);
        Assert.Equal(77, p.CurrentHp);
    }

    public static void Test_TurnFlow_CombatEndDispatchesInTheGamesOrderAndClearsThePlayer()
    {
        var log = new List<string>();
        var sim = Create(31UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 6));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        Assert.NotNull(PowerCmd.Apply<StrengthPower>(cs, p, 2m, p, null));
        p.Block = 7;
        string? atCombatEnd = null;
        probe.OnEvent = (state, tag) =>
        {
            if (tag == "AfterCombatEnd")
            {
                var player = state.Player;
                atCombatEnd = $"{state.IsInProgress}|{state.Outcome}|{player.Phase}|{player.Hand.Count}|{player.PlayPile.Count}|{player.Powers.Count}|{player.Block}|{state.History.Count > 0}";
            }
        };
        sim.Step(GameAction.Play(0, 0));
        Assert.Equal(CombatOutcome.Victory, sim.Outcome);
        Assert.Equal("False|Victory|None|4|1|2|7|True", atCombatEnd, "AfterCombatEnd runs before the history and the player are cleared");
        Assert.False(log.Contains("AfterCombatVictoryEarly"), "the player's powers are removed before AfterCombatVictory");
        Assert.False(log.Contains("AfterCombatVictory"));
        foreach (var pile in p.AllPiles)
        {
            Assert.Equal(0, pile.Count, $"Player.AfterCombatEnd clears the {pile.Type} pile");
        }
        Assert.Equal(0, p.Block, "the player loses its block");
        Assert.Equal(0, p.Powers.Count, "the player loses every power");
        Assert.Equal(0, cs.History.Count, "the combat history is cleared");
        Assert.Equal(0, cs.PlayersTakingExtraTurn.Count);
        Assert.Equal(PlayerTurnPhase.None, p.Phase);
        Assert.False(cs.IsInProgress);
        Assert.Equal(10, p.Deck.Count, "the deck survives the combat");
        Assert.Equal(sim.StateHash(), sim.Clone().StateHash());

        W1Pending.Run("TurnFlowTests.Test_TurnFlow_CombatEndDispatchesInTheGamesOrderAndClearsThePlayer (relics)", "hooks", () =>
        {
            var relicLog = new List<string>();
            var relicSim = Create(31UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 6));
            var rcs = relicSim.State;
            var relic = new CombatEndRelic(relicLog);
            rcs.Player.AddRelicInternal(relic);
            var listeners = new List<AbstractModel>();
            Hook.IterateHookListeners(rcs, listeners);
            if (!listeners.Contains(relic))
            {
                throw new NotImplementedException("relics are not combat listeners yet: hooks (ListenerSnapshot, CONTRACTS Part B section B3)");
            }
            relicSim.Step(GameAction.Play(0, 0));
            Assert.SequenceEqual(new[] { "AfterCombatEnd:4:True", "AfterCombatVictoryEarly:0:False", "AfterCombatVictory:0:False" }, relicLog, "AfterCombatEnd, then History.Clear and Player.AfterCombatEnd, then the two victory passes");
        });
    }

    public static void Test_TurnFlow_ReviveBeforeCombatEndHealsADeadPlayer()
    {
        var sim = Create(37UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 30));
        var cs = sim.State;
        var p = cs.Player;
        p.CurrentHp = 0;
        CreatureCmd.Kill(cs, cs.Monsters[0]);
        Assert.Equal(CombatOutcome.InProgress, cs.Outcome, "no loss is pending: the player was not killed");
        CombatLoop.CheckWinCondition(cs);
        Assert.Equal(CombatOutcome.Victory, cs.Outcome);
        Assert.Equal(1, p.CurrentHp, "ReviveBeforeCombatEnd heals a dead player to 1 HP");
        Assert.True(p.IsActiveForHooks);
    }

    public static void Test_TurnFlow_SlyDiscardAutoPlaysTheCard()
    {
        var log = new List<string>();
        var sim = Create(41UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60), new Dummy("TURN_FLOW_DUMMY_B", 60));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        var targets = new List<int>();
        var sly = cs.CreateCard(new ProbeCard("TURN_FLOW_SLY_STRIKE", CardType.Attack, TargetType.AnyEnemy, 1, new[] { CardKeyword.Sly }, (state, play) =>
        {
            targets.Add(play.Target!.CombatId);
            DamageCmd.Attack(state, 4m).FromCard(play.Card, play).Targeting(play.Target!).Execute();
        }), p);
        CardPileCmd.AddGeneratedCardToCombat(cs, sly, PileType.Hand);
        var expectedRng = cs.Rng.CombatTargets.DeepClone();
        int expectedIndex = expectedRng.NextInt(0, cs.Monsters.Count);
        var expectedTarget = cs.Monsters[expectedIndex];
        int energy = p.Energy;
        log.Clear();
        probe.Plays.Clear();
        CardCmd.Discard(cs, sly);
        Assert.SequenceEqual(new[] { expectedTarget.CombatId }, targets, "the auto-play draws its target from Rng.CombatTargets");
        Assert.Equal(expectedRng.Counter, cs.Rng.CombatTargets.Counter);
        Assert.Equal(56, expectedTarget.CurrentHp);
        Assert.SequenceEqual(
            new[]
            {
                "AfterCardDiscarded:TURN_FLOW_SLY_STRIKE",
                "ShouldPlay:TURN_FLOW_SLY_STRIKE:SlyDiscard",
                $"BeforeCardAutoPlayed:TURN_FLOW_SLY_STRIKE:SlyDiscard:{expectedTarget.CombatId}",
                "BeforeCardPlayed:TURN_FLOW_SLY_STRIKE:auto=True",
                "AfterCardPlayed:TURN_FLOW_SLY_STRIKE"
            },
            log.Where(t => t.Contains("TURN_FLOW_SLY_STRIKE", StringComparison.Ordinal) && !t.StartsWith("AfterCardChangedPiles", StringComparison.Ordinal)));
        var play = Assert.NotNull(probe.Plays.SingleOrDefault());
        Assert.True(play.IsAutoPlay);
        Assert.Equal(new ResourceInfo(0, 1, 0, 0), play.Resources, "an auto-play spends nothing and values the card at its cost");
        Assert.Equal(energy, p.Energy);
        Assert.True(ReferenceEquals(sly, p.DiscardPile[p.DiscardPile.Count - 1]), "the played card returns to the discard pile");
        var discarded = cs.History.CardDiscardedEntries;
        Assert.Equal(sly.InstanceId, discarded[discarded.Count - 1].CardInstanceId);
        var started = cs.History.CardPlayStartedEntries;
        Assert.True(started[started.Count - 1].CardPlay.IsAutoPlay);
        Assert.True(discarded[discarded.Count - 1].Stamp.Sequence < started[started.Count - 1].Stamp.Sequence, "the discard is recorded before the auto-play");
    }

    public static void Test_TurnFlow_AutoPlayMovesUnplayableAndBlockedCardsWithoutPlaying()
    {
        var log = new List<string>();
        var sim = Create(43UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        var played = new List<string>();
        Action<CombatState, CardPlay> onPlay = (_, play) => played.Add(play.Card.Id);
        var unplayable = AddToHand(cs, new ProbeCard("TURN_FLOW_UNPLAYABLE", CardType.Status, TargetType.None, 0, new[] { CardKeyword.Unplayable }, onPlay));
        var exhausting = AddToHand(cs, new ProbeCard("TURN_FLOW_UNPLAYABLE_EXHAUST", CardType.Status, TargetType.None, 0, new[] { CardKeyword.Unplayable, CardKeyword.Exhaust }, onPlay));
        var blocked = AddToHand(cs, new ProbeCard("TURN_FLOW_BLOCKED", CardType.Skill, TargetType.Self, 1, Array.Empty<CardKeyword>(), onPlay));
        var attack = AddToHand(cs, new ProbeCard("TURN_FLOW_NO_TARGET", CardType.Attack, TargetType.AnyEnemy, 1, Array.Empty<CardKeyword>(), onPlay));
        probe.AllowPlay = (card, _) => card.Id != "TURN_FLOW_BLOCKED";
        log.Clear();
        CardCmd.AutoPlay(cs, unplayable, null);
        CardCmd.AutoPlay(cs, exhausting, null);
        CardCmd.AutoPlay(cs, blocked, null);
        probe.AllowHitting = _ => false;
        int targetDraws = cs.Rng.CombatTargets.Counter;
        CardCmd.AutoPlay(cs, attack, null);
        probe.AllowHitting = null;
        Assert.Equal(0, played.Count, "none of these cards is played");
        Assert.True(p.DiscardPile.Contains(unplayable), "an unplayable card goes to the discard pile");
        Assert.True(p.ExhaustPile.Contains(exhausting), "an unplayable Exhaust card is exhausted");
        Assert.True(p.DiscardPile.Contains(blocked), "a card that ShouldPlay blocks goes to the discard pile");
        Assert.True(p.DiscardPile.Contains(attack), "an attack without a hittable enemy goes to the discard pile");
        Assert.Equal(targetDraws, cs.Rng.CombatTargets.Counter, "an empty target list draws nothing");
        Assert.True(log.Contains("ShouldPlay:TURN_FLOW_BLOCKED:Default"));
        Assert.False(log.Any(t => t.StartsWith("BeforeCardAutoPlayed", StringComparison.Ordinal)), "no card reached BeforeCardAutoPlayed");
        Assert.Equal(exhausting.InstanceId, cs.History.CardExhaustedEntries[cs.History.CardExhaustedEntries.Count - 1].CardInstanceId);
    }

    public static void Test_TurnFlow_AutoPhaseHooksSeeTheirPhases()
    {
        var log = new List<string>();
        var sim = Create(47UL, Array.Empty<string>(), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var probe = AttachProbe(cs, cs.Player, log);
        log.Clear();
        sim.Step(GameAction.EndTurn);
        var end = log.TakeWhile(t => t != "BeforeSideTurnStart:Enemy").Where(t => !t.StartsWith("AfterCardChangedPiles", StringComparison.Ordinal)).ToList();
        Assert.SequenceEqual(new[] { "AfterAutoPostPlayPhaseEntered:AutoPostPlay", "BeforeSideTurnEnd:Player", "BeforeFlush", "AfterSideTurnEnd:Player" }, end.Where(t => t.StartsWith("After", StringComparison.Ordinal) || t.StartsWith("Before", StringComparison.Ordinal)).Where(t => !t.StartsWith("BeforeHandDraw", StringComparison.Ordinal)));
        Assert.Equal(PlayerTurnPhase.End, probe.Sides.First(s => s.Hook == "BeforeSideTurnEnd:Player").Phase, "the turn end runs in the End phase");
        var turn = TurnStartSlice(log);
        Assert.SequenceEqual(
            new[] { "AfterHandEmptied:depth0", "AfterAutoPrePlayPhaseEnteredEarly:AutoPrePlay", "AfterAutoPrePlayPhaseEntered:AutoPrePlay", "AfterAutoPrePlayPhaseEnteredLate:AutoPrePlay" },
            turn.Where(t => t.StartsWith("AfterHandEmptied", StringComparison.Ordinal) || t.StartsWith("AfterAutoPrePlay", StringComparison.Ordinal)),
            "the empty-hand check comes first, then the three pre-play passes");
        Assert.Equal(PlayerTurnPhase.Play, cs.Player.Phase);
    }

    public static void Test_TurnFlow_ResultLocationHookRedirectsAPlay()
    {
        var log = new List<string>();
        var sim = Create(53UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        var probe = AttachProbe(cs, p, log);
        probe.Redirect = (card, location) => card.Id == Strike ? new CardLocation(PileType.Exhaust, CardPilePosition.Bottom) : location;
        var strike = p.Hand[0];
        log.Clear();
        sim.Step(GameAction.Play(0, 0));
        Assert.True(p.ExhaustPile.Contains(strike), "the modified result location exhausts the Strike");
        Assert.Equal(0, p.DiscardPile.Count);
        Assert.True(log.Contains("AfterModifyingCardPlayResultLocation:STRIKE_IRONCLAD:Exhaust"), "the modifier hears the final location");
        Assert.True(log.Contains("AfterCardExhausted:STRIKE_IRONCLAD:False"));
        Assert.Equal(strike.InstanceId, cs.History.CardExhaustedEntries[cs.History.CardExhaustedEntries.Count - 1].CardInstanceId);
    }

    public static void Test_TurnFlow_HistoryRecordsTheLoopSites()
    {
        var sim = Create(59UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 100));
        var cs = sim.State;
        var p = cs.Player;
        var drawn = cs.History.CardDrawnEntries;
        Assert.Equal(5, drawn.Count, "the opening hand draw records five draws");
        Assert.True(drawn.All(e => e.FromHandDraw));
        Assert.SequenceEqual(p.Hand.Cards.Select(c => c.InstanceId), drawn.Select(e => e.CardInstanceId), "draws are recorded in draw order");
        Assert.True(drawn.All(e => e.Stamp.ActorId == p.CombatId), "a drawn card's actor is its owner");

        var strike = p.Hand[0];
        sim.Step(GameAction.Play(0, 0));
        var started = cs.History.CardPlayStartedEntries;
        var finished = cs.History.CardPlayFinishedEntries;
        Assert.Equal(1, started.Count);
        Assert.Equal(1, finished.Count);
        Assert.Equal(strike.InstanceId, started[0].CardPlay.CardInstanceId);
        Assert.Equal(started[0].CardPlay.PlayId, finished[0].CardPlay.PlayId, "start and finish record the same play");
        Assert.Equal(new ResourceInfo(1, 1, 0, 0), started[0].CardPlay.Resources);
        Assert.False(started[0].CardPlay.IsAutoPlay);
        Assert.True(started[0].Stamp.Sequence < finished[0].Stamp.Sequence);

        var bane = cs.CreateCard(CardRegistry.Get(AscendersBane.CardId), p);
        CardPileCmd.AddGeneratedCardToCombat(cs, bane, PileType.Hand, p);
        Assert.Equal(bane.InstanceId, cs.History.CardGeneratedEntries[0].CardInstanceId);
        Assert.Equal(p.CombatId, cs.History.CardGeneratedEntries[0].CreatorId);
        sim.Step(GameAction.EndTurn);
        Assert.Equal(bane.InstanceId, cs.History.CardExhaustedEntries[0].CardInstanceId, "the ethereal exhaust at the turn end is recorded");
        Assert.Equal(0, cs.History.CardDiscardedEntries.Count, "the hand flush is a pile move, not a discard");
        Assert.Equal(10, cs.History.CardDrawnEntries.Count, "the turn-2 draw is recorded");

        CreatureCmd.Kill(cs, cs.Monsters[0]);
        CombatLoop.CheckWinCondition(cs);
        Assert.Equal(0, cs.History.Count, "the victory clears the history");
    }

    public static void Test_TurnFlow_AfterCardChangedPilesFollowsEveryMove()
    {
        var log = new List<string>();
        var sim = Create(61UL, Repeat(Strike, 6), new Dummy("TURN_FLOW_DUMMY_A", 100));
        var cs = sim.State;
        var p = cs.Player;
        AttachProbe(cs, p, log);
        var strike = p.Hand[0];
        log.Clear();
        sim.Step(GameAction.Play(0, 0));
        Assert.SequenceEqual(new[] { $"AfterCardChangedPiles:{strike.InstanceId}:Hand->Play", $"AfterCardChangedPiles:{strike.InstanceId}:Play->Discard" }, Moves(log));

        log.Clear();
        CardPileCmd.Draw(cs, 1m, p);
        var top = p.Hand[p.Hand.Count - 1];
        Assert.SequenceEqual(new[] { $"AfterCardChangedPiles:{top.InstanceId}:Draw->Hand" }, Moves(log));

        log.Clear();
        var hand = p.Hand.Cards.ToList();
        CardPileCmd.Add(cs, hand, PileType.Discard);
        Assert.Equal(hand.Count, Moves(log).Count, "a batch reports each moved card after the whole batch moved");
        log.Clear();
        int before = p.DiscardPile.Count;
        CardPileCmd.Shuffle(cs, p);
        Assert.Equal(before, Moves(log).Count, "a shuffle reports only the cards that came from the discard pile");
        Assert.True(Moves(log).All(m => m.EndsWith("Discard->Draw", StringComparison.Ordinal)));
    }

    public static void Test_TurnFlow_GeneratedCardsAreRecordedOwnedAndRefusedOutsideTheCombat()
    {
        var log = new List<string>();
        var sim = Create(67UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        AttachProbe(cs, p, log);
        var cards = new[] { cs.CreateCard(CardRegistry.Get(Strike), p), cs.CreateCard(CardRegistry.Get(Strike), p) };
        log.Clear();
        var results = CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Discard, p);
        Assert.Equal(2, results.Count);
        Assert.True(results.All(r => r.Success));
        Assert.SequenceEqual(cards.Select(c => c.InstanceId), cs.History.CardGeneratedEntries.Select(e => e.CardInstanceId));
        Assert.SequenceEqual(
            new[] { "AfterCardEnteredCombat:STRIKE_IRONCLAD", "AfterCardGeneratedForCombat:STRIKE_IRONCLAD:0", "AfterCardEnteredCombat:STRIKE_IRONCLAD", "AfterCardGeneratedForCombat:STRIKE_IRONCLAD:0" },
            log.Where(t => t.StartsWith("AfterCardEnteredCombat", StringComparison.Ordinal) || t.StartsWith("AfterCardGenerated", StringComparison.Ordinal)),
            "each card is added, then announced, one after the other");
        Assert.Throws<InvalidOperationException>(() => CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, p));
        Assert.Throws<InvalidOperationException>(() => CardPileCmd.AddGeneratedCardsToCombat(cs, new[] { cs.CreateCard(CardRegistry.Get(Strike), p) }, PileType.Deck, p));

        var refused = cs.CreateCard(CardRegistry.Get(Strike), p);
        cs.IsInProgress = false;
        var none = CardPileCmd.AddGeneratedCardToCombat(cs, refused, PileType.Hand);
        cs.IsInProgress = true;
        Assert.False(none.Success);
        Assert.Null(p.FindPileContaining(refused));
        Assert.True(cs.Run.Fidelity.Entries.Any(e => e.Site == GeneratedOutsideCombatSite), "the game would index an empty result list here; the sim flags the site");
    }

    public static void Test_TurnFlow_AutoPlayFromDrawPilePlaysTheTopCards()
    {
        var sim = Create(71UL, Repeat(Strike, 10), new Dummy("TURN_FLOW_DUMMY_A", 100));
        var cs = sim.State;
        var p = cs.Player;
        var top = p.DrawPile[0];
        var second = p.DrawPile[1];
        int energy = p.Energy;
        int targetDraws = cs.Rng.CombatTargets.Counter;
        CardPileCmd.AutoPlayFromDrawPile(cs, p, 2, CardPilePosition.Top, forceExhaust: true);
        Assert.True(p.ExhaustPile.Contains(top) && p.ExhaustPile.Contains(second), "forced exhaust sends both auto-played cards to the exhaust pile");
        Assert.Equal(88, cs.Monsters[0].CurrentHp, "both Strikes hit");
        Assert.Equal(energy, p.Energy);
        Assert.Equal(targetDraws + 2, cs.Rng.CombatTargets.Counter, "each untargeted attack draws one target");
        Assert.Equal(2, cs.History.CardPlayStartedEntries.Count(e => e.CardPlay.IsAutoPlay));
    }

    public static void Test_TurnFlow_PotionStepsRouteThroughPotionActions()
    {
        W1Pending.Run("TurnFlowTests.Test_TurnFlow_PotionStepsRouteThroughPotionActions", "relics-potions", () =>
        {
            var sim = Create(73UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
            var cs = sim.State;
            var p = cs.Player;
            var potion = new ProbePotion();
            Assert.True(p.AddPotionInternal(potion, 0).Success);
            CombatLoop.ExecuteResolvedAction(cs, GameAction.UsePotion(0), p);
            Assert.Null(p.PotionSlots[0], "the used potion leaves its slot");
            Assert.Equal(1, cs.History.PotionUsedEntries.Count);
            var second = new ProbePotion();
            Assert.True(p.AddPotionInternal(second, 1).Success);
            CombatLoop.ExecuteResolvedAction(cs, GameAction.DiscardPotion(1), null);
            Assert.Null(p.PotionSlots[1], "the discarded potion leaves its slot");
        });
    }

    public static void Test_TurnFlow_AddPetRegistersThePetOnItsOwner()
    {
        var sim = Create(79UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        var stranger = new PlayerState(7, StartingDecks.IroncladCharacterId, 10, 10, 3);
        var owned = new MonsterState(cs.AllocateCreatureId(), new Dummy("TURN_FLOW_PET", 10), 10, 10, null, null)
        {
            Side = CombatSide.Player,
            PetOwnerId = stranger.CombatId
        };
        Assert.Throws<InvalidOperationException>(() => PlayerCmd.AddPet(cs, owned, p));
        Assert.Equal(0, p.PetIds.Count, "a pet owned by another player is refused before it is registered");

        W1Pending.Run("TurnFlowTests.Test_TurnFlow_AddPetRegistersThePetOnItsOwner", "monsters-kernel", () =>
        {
            var pet = PlayerCmd.AddPet(cs, p, new Dummy("TURN_FLOW_PET", 10));
            Assert.Equal(p.CombatId, pet.PetOwnerId);
            Assert.SequenceEqual(new[] { pet.CombatId }, p.PetIds);
            Assert.True(cs.Allies.Contains(pet), "the pet joins the player's side");
            Assert.Equal(CombatSide.Player, pet.Side);
            Assert.Equal(10, pet.CurrentHp);
        });
    }

    public static void Test_TurnFlow_DeckAddAndRemoveWriteThePlayersDeck()
    {
        var sim = Create(83UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
        var cs = sim.State;
        var p = cs.Player;
        Assert.Throws<InvalidOperationException>(() => CardPileCmd.RemoveFromDeck(cs, p.Hand[0]), "a combat copy is not a deck card");
        Assert.Throws<ArgumentException>(() => CardPileCmd.AddCurseToDeck(cs.Run, Strike, p), "only curses go through AddCurseToDeck");

        W1Pending.Run("TurnFlowTests.Test_TurnFlow_DeckAddAndRemoveWriteThePlayersDeck", "hooks", () =>
        {
            int before = p.Deck.Count;
            var added = Assert.NotNull(CardPileCmd.AddCurseToDeck(cs.Run, AscendersBane.CardId, p));
            Assert.Equal(before + 1, p.Deck.Count);
            Assert.True(ReferenceEquals(added, p.Deck[p.Deck.Count - 1]), "the curse is appended to the deck");
            Assert.True(ReferenceEquals(p, added.Owner));
            Assert.Null(p.FindPileContaining(added), "a deck add does not touch the combat piles");
            var removed = p.Deck[0];
            CardPileCmd.RemoveFromDeck(cs, removed);
            Assert.Equal(before, p.Deck.Count);
            Assert.True(removed.HasBeenRemovedFromState);
            Assert.False(p.Deck.Contains(removed));
        });
    }

    public static void Test_TurnFlow_OrbQueueTriggersRunAtTheGamesPoints()
    {
        W1Pending.Run("TurnFlowTests.Test_TurnFlow_OrbQueueTriggersRunAtTheGamesPoints", "character-mechanics", () =>
        {
            var log = new List<string>();
            var sim = Create(89UL, Repeat(Defend, 10), new Dummy("TURN_FLOW_DUMMY_A", 60));
            var cs = sim.State;
            var p = cs.Player;
            AttachProbe(cs, p, log);
            var orb = new ProbeOrb(log);
            orb.AttachTo(p);
            p.OrbQueue.AddCapacity(1);
            Assert.True(p.OrbQueue.TryEnqueue(orb));
            log.Clear();
            sim.Step(GameAction.EndTurn);
            Assert.True(log.IndexOf("Orb:BeforeTurnEnd") < log.IndexOf("BeforeFlush"), "the turn-end trigger runs in DoTurnEnd");
            var turn = TurnStartSlice(log);
            Assert.True(turn.IndexOf("AfterSideTurnStart:Player") < turn.IndexOf("Orb:AfterTurnStart"), "the turn-start trigger follows AfterSideTurnStart");
            Assert.True(turn.IndexOf("Orb:AfterTurnStart") < turn.IndexOf("AfterAutoPrePlayPhaseEntered:AutoPrePlay"), "and precedes the pre-play phase");
        });
    }

    private static CombatSim Create(ulong seed, string[] deck, params MonsterDef[] monsters)
        => CombatSim.Create(seed, deck, Encounter, 0, Spawn(monsters));

    private static Action<CombatState> Spawn(params MonsterDef[] defs) => cs =>
    {
        foreach (var def in defs)
        {
            int hp = def.MaxInitialHp(cs);
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, hp, hp, null, null));
        }
    };

    private static Probe AttachProbe(CombatState cs, CreatureState owner, List<string> log)
    {
        var probe = new Probe(log);
        probe.ApplyInternal(owner, 1m);
        return probe;
    }

    private static CardInstance AddToHand(CombatState cs, CardDef def)
    {
        var card = cs.CreateCard(def, cs.Player);
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand);
        return card;
    }

    private static List<CardInstance> CombatCards(PlayerState p) => p.AllPiles.SelectMany(pile => pile.Cards).ToList();

    private static int[] Ids(IReadOnlyList<CreatureState> creatures) => creatures.Select(c => c.CombatId).ToArray();

    private static string[] Repeat(string id, int count) => Enumerable.Repeat(id, count).ToArray();

    private static List<string> Moves(List<string> log) => log.Where(t => t.StartsWith("AfterCardChangedPiles", StringComparison.Ordinal)).ToList();

    private static List<string> TurnStartSlice(List<string> log)
    {
        int start = log.LastIndexOf("BeforeSideTurnStart:Player");
        return start < 0 ? new List<string>() : log.Skip(start).ToList();
    }

    private static bool IsStartStep(string tag)
        => tag == "pause" || tag == "resume"
            || tag.StartsWith("BeforeSideTurnStart", StringComparison.Ordinal)
            || tag.StartsWith("AfterSideTurnStart", StringComparison.Ordinal) && !tag.StartsWith("AfterSideTurnStartLate", StringComparison.Ordinal)
            || tag.StartsWith("BeforeHandDraw", StringComparison.Ordinal)
            || tag.StartsWith("AfterPlayerTurnStart", StringComparison.Ordinal)
            || tag.StartsWith("AfterAutoPrePlay", StringComparison.Ordinal);

    private static string PileName(CombatState? cs, CardInstance card)
    {
        if (cs == null)
        {
            return "?";
        }
        var pile = cs.Player.FindPileContaining(card);
        if (pile != null)
        {
            return pile.Type.ToString();
        }
        return cs.Player.Deck.Contains(card) ? "Deck" : "None";
    }

    private sealed class Probe : PowerModel
    {
        private readonly List<string> _log;

        public Probe(List<string> log)
        {
            _log = log;
        }

        public override string Id => "TURN_FLOW_PROBE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public Action<CombatState, string>? OnEvent { get; set; }

        public Func<CardInstance, AutoPlayType, bool>? AllowPlay { get; set; }

        public Func<CardInstance, CardLocation, CardLocation>? Redirect { get; set; }

        public Func<bool>? ExtraTurn { get; set; }

        public Func<CreatureState, bool>? AllowHitting { get; set; }

        public List<(string Hook, IReadOnlyList<CreatureState> Participants, int LiveMonsters, PlayerTurnPhase Phase)> Sides { get; } = new();

        public List<CardPlay> Plays { get; } = new();

        public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => Side(cs, "BeforeSideTurnStart", side, participants);

        public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => Side(cs, "AfterSideTurnStart", side, participants);

        public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => Side(cs, "BeforeSideTurnEnd", side, participants);

        public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => Side(cs, "AfterSideTurnEnd", side, participants);

        public override void BeforeHandDraw(CombatState cs, PlayerState player) => Log(cs, $"BeforeHandDraw:t{player.TurnNumber}");

        public override void AfterPlayerTurnStart(CombatState cs, PlayerState player) => Log(cs, $"AfterPlayerTurnStart:t{player.TurnNumber}");

        public override void AfterAutoPrePlayPhaseEnteredEarly(CombatState cs, PlayerState player) => Log(cs, $"AfterAutoPrePlayPhaseEnteredEarly:{player.Phase}");

        public override void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player) => Log(cs, $"AfterAutoPrePlayPhaseEntered:{player.Phase}");

        public override void AfterAutoPrePlayPhaseEnteredLate(CombatState cs, PlayerState player) => Log(cs, $"AfterAutoPrePlayPhaseEnteredLate:{player.Phase}");

        public override void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player) => Log(cs, $"AfterAutoPostPlayPhaseEntered:{player.Phase}");

        public override void BeforeFlush(CombatState cs, PlayerState player) => Log(cs, "BeforeFlush");

        public override void AfterHandEmptied(CombatState cs, PlayerState player) => Log(cs, $"AfterHandEmptied:depth{cs.CardOrPotionEffectDepth}");

        public override bool ShouldTakeExtraTurn(CombatState cs, PlayerState player) => ExtraTurn?.Invoke() ?? false;

        public override void AfterTakingExtraTurn(CombatState cs, PlayerState player) => Log(cs, $"AfterTakingExtraTurn:{player.CombatId}");

        public override void AfterCombatEnd(RunState rs, CombatRoom? room) => Log(rs.Combat!, "AfterCombatEnd");

        public override void AfterCombatVictoryEarly(RunState rs, CombatRoom? room) => Log(rs.Combat!, "AfterCombatVictoryEarly");

        public override void AfterCombatVictory(RunState rs, CombatRoom? room) => Log(rs.Combat!, "AfterCombatVictory");

        public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
        {
            if (autoPlayType != AutoPlayType.None)
            {
                Log(cs, $"ShouldPlay:{card.Id}:{autoPlayType}");
            }
            return AllowPlay?.Invoke(card, autoPlayType) ?? true;
        }

        public override void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type) => Log(cs, $"BeforeCardAutoPlayed:{card.Id}:{type}:{target?.CombatId ?? -1}");

        public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy) => Log(rs.Combat!, $"AfterCardChangedPiles:{card.InstanceId}:{oldPileType}->{PileName(rs.Combat, card)}");

        public override CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
            => Redirect?.Invoke(card, cardLocation) ?? cardLocation;

        public override void AfterModifyingCardPlayResultLocation(CombatState cs, CardInstance card, CardLocation cardLocation) => Log(cs, $"AfterModifyingCardPlayResultLocation:{card.Id}:{cardLocation.PileType}");

        public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay) => Log(cs, $"BeforeCardPlayed:{cardPlay.Card.Id}:auto={cardPlay.IsAutoPlay}");

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
        {
            Plays.Add(cardPlay);
            Log(cs, $"AfterCardPlayed:{cardPlay.Card.Id}");
        }

        public override void AfterCardDiscarded(CombatState cs, CardInstance card) => Log(cs, $"AfterCardDiscarded:{card.Id}");

        public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal) => Log(cs, $"AfterCardExhausted:{card.Id}:{causedByEthereal}");

        public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator) => Log(cs, $"AfterCardGeneratedForCombat:{card.Id}:{creator?.CombatId ?? -1}");

        public override void AfterCardEnteredCombat(CombatState cs, CardInstance card) => Log(cs, $"AfterCardEnteredCombat:{card.Id}");

        public override bool ShouldAllowHitting(CombatState cs, CreatureState creature) => AllowHitting?.Invoke(creature) ?? true;

        private void Side(CombatState cs, string hook, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            string tag = $"{hook}:{side}";
            Sides.Add((tag, participants, cs.Monsters.Count, cs.Player.Phase));
            Log(cs, tag);
        }

        private void Log(CombatState cs, string tag)
        {
            _log.Add(tag);
            OnEvent?.Invoke(cs, tag);
        }
    }

    private sealed class ProbeCard : CardDef
    {
        private readonly IReadOnlyCollection<CardKeyword> _keywords;
        private readonly Action<CombatState, CardPlay>? _onPlay;

        public ProbeCard(string id, CardType type, TargetType targetType, int cost, IReadOnlyCollection<CardKeyword> keywords, Action<CombatState, CardPlay>? onPlay)
            : base(id, cost, type, CardRarity.Common, targetType)
        {
            _keywords = keywords;
            _onPlay = onPlay;
        }

        public override int MaxUpgradeLevel => 0;

        public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => _keywords;

        public override void OnPlay(CombatState cs, CardPlay play) => _onPlay?.Invoke(cs, play);
    }

    private sealed class CombatEndRelic : RelicModel
    {
        private readonly List<string> _log;

        public CombatEndRelic(List<string> log)
        {
            _log = log;
        }

        public override string Id => "TURN_FLOW_COMBAT_END_RELIC";

        public override RelicRarity Rarity => RelicRarity.Common;

        public override void AfterCombatEnd(RunState rs, CombatRoom? room) => Record(rs, "AfterCombatEnd");

        public override void AfterCombatVictoryEarly(RunState rs, CombatRoom? room) => Record(rs, "AfterCombatVictoryEarly");

        public override void AfterCombatVictory(RunState rs, CombatRoom? room) => Record(rs, "AfterCombatVictory");

        private void Record(RunState rs, string hook)
        {
            var player = rs.Players[0];
            _log.Add($"{hook}:{player.Hand.Count}:{rs.Combat!.History.Count > 0}");
        }
    }

    private sealed class ProbePotion : PotionModel
    {
        public override string Id => "TURN_FLOW_PROBE_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.Self;
    }

    private sealed class ProbeOrb : OrbModel
    {
        private readonly List<string> _log;

        public ProbeOrb(List<string> log)
        {
            _log = log;
        }

        public override string Id => "TURN_FLOW_PROBE_ORB";

        public override decimal PassiveVal(CombatState cs) => 0m;

        public override decimal EvokeVal(CombatState cs) => 0m;

        public override void BeforeTurnEndOrbTrigger(CombatState cs) => _log.Add("Orb:BeforeTurnEnd");

        public override void AfterTurnStartOrbTrigger(CombatState cs) => _log.Add("Orb:AfterTurnStart");
    }

    private sealed class LoggingAnswerer : IChoiceAnswerer
    {
        private readonly List<string> _log;
        private readonly int _pick;

        public LoggingAnswerer(List<string> log, int pick)
        {
            _log = log;
            _pick = pick;
        }

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            _log.Add("answer");
            return new ChoiceAnswer(new[] { _pick }, false);
        }
    }

    private sealed class Dummy : MonsterDef
    {
        public const string WaitMoveId = "TURN_FLOW_WAIT";
        public const string BiteMoveId = "TURN_FLOW_BITE";

        private readonly int _hp;
        private readonly int _bite;
        private readonly List<string>? _log;

        public Dummy(string id, int hp, int bite = 0, List<string>? log = null)
            : base(id)
        {
            _hp = hp;
            _bite = bite;
            _log = log;
        }

        public override int MinInitialHp(CombatState cs) => _hp;

        public override int MaxInitialHp(CombatState cs) => _hp;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var log = _log;
            string id = Id;
            int damage = _bite;
            MoveStateDef move = damage > 0
                ? new MoveStateDef(BiteMoveId, (c, s, targets) =>
                {
                    log?.Add(id);
                    DamageCmd.Attack(c, damage).FromMonster(s).Execute();
                }, new SingleAttackIntent(damage)) { FollowUpStateId = BiteMoveId }
                : new MoveStateDef(WaitMoveId, (c, s, targets) => log?.Add(id)) { FollowUpStateId = WaitMoveId };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { move }, move);
        }
    }
}
