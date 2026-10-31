using System.Text;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class LoopTests
{
    private const string TestEncounterId = "LOOP_TEST_ENCOUNTER";
    private const string Strike = StrikeIronclad.CardId;
    private const string Defend = DefendIronclad.CardId;
    private const string BashId = Bash.CardId;

    public static void Test_Loop_SetUpThenStartReachesFirstDecision()
    {
        var cs = CombatLoop.SetUpCombat(5UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var p = cs.Player;
        Assert.Equal(10, p.DrawPile.Count, "the whole deck starts in the draw pile");
        Assert.Equal(0, p.Hand.Count);
        Assert.Equal(PlayerTurnPhase.None, p.Phase);
        Assert.Equal(0, p.Energy);
        Assert.Equal(1, p.TurnNumber);
        Assert.Equal(1, cs.RoundNumber);
        Assert.Equal(CombatSide.Player, cs.CurrentSide);
        Assert.Equal(1, cs.Monsters.Count);
        Assert.True(cs.Monsters[0].SpawnedThisTurn, "monsters are set up as spawned this turn");
        Assert.Equal(1, cs.Monsters[0].CombatId, "player is creature 0, monsters follow");
        Assert.True(cs.Monsters[0].HasAi, "the move graph is built in SetUpCombat");
        Assert.Null(cs.Monsters[0].Ai.NextMove, "no move is rolled before the combat starts");
        Assert.True(cs.IsStarting, "SetUpCombat leaves the combat starting");
        Assert.False(cs.IsInProgress, "SetUpCombat does not start the combat");
        Assert.False(CombatStatus.IsEnding(cs), "a combat that is not in progress is not ending");
        Assert.True(CombatStatus.IsOverOrEnding(cs), "a combat that is not in progress counts as over or ending");
        var buffer = new List<GameAction>();
        LegalActionGenerator.Fill(cs, buffer);
        Assert.Equal(0, buffer.Count, "no decision exists before the combat starts");
        Assert.Throws<InvalidOperationException>(() => CombatLoop.Step(cs, GameAction.EndTurn));

        CombatLoop.StartCombat(cs);
        Assert.False(cs.IsStarting);
        Assert.True(cs.IsInProgress);
        Assert.Throws<InvalidOperationException>(() => CombatLoop.StartCombat(cs));
        Assert.Equal(CombatOutcome.InProgress, cs.Outcome);
        Assert.Equal(PlayerTurnPhase.Play, p.Phase);
        Assert.Equal(3, p.Energy, "Ironclad max energy");
        Assert.Equal(80, p.CurrentHp);
        Assert.Equal(80, p.MaxHp);
        Assert.Equal(5, p.Hand.Count, "base hand draw");
        Assert.Equal(5, p.DrawPile.Count);
        Assert.Equal(LoopDummy.WaitMoveId, cs.Monsters[0].Ai.NextMove?.Id, "the first move is rolled when the creature is added");
        Assert.True(cs.Monsters[0].SpawnedThisTurn, "spawned flag clears only on the first side switch");
    }

    public static void Test_Loop_SetUpWindowGatesCommandsLikeTheGame()
    {
        var probes = new List<SetupWindowProbe>();
        var sim = CombatSim.Create(59UL, Repeat(Strike, 10), TestEncounterId, 0, cs =>
        {
            Spawn(new SetupProbeMonster("LOOP_SETUP_PROBE", 40, probes))(cs);
            var spawned = cs.Monsters[0];
            Assert.NotNull(PowerCmd.Apply<DexterityPower>(cs, spawned, 3m, spawned, null), "PowerCmd.Apply checks only IsEnding, which is false before the combat exists");
            probes.Add(ProbeSetupWindow(cs, spawned));
        });
        var s = sim.State;
        var m = s.Monsters[0];
        Assert.Equal(2, probes.Count, "one probe while the encounter spawns, one from AfterAddedToRoom");

        var spawn = probes[0];
        Assert.False(spawn.IsStarting, "no turn state exists while the encounter spawns");
        Assert.False(spawn.IsInProgress);
        Assert.False(spawn.IsEnding);
        Assert.True(spawn.IsOverOrEnding);
        Assert.Equal(5m, spawn.ModifiedBlock, "guarded listeners are silenced before SetUpCombat");
        Assert.Equal(0m, spawn.BlockGained, "GainBlock is a no-op while over or ending");
        Assert.True(spawn.StrengthApplied);
        Assert.Equal(0, spawn.CardsDrawn);
        Assert.False(spawn.GeneratedCardAdded, "generated cards need a combat in progress");

        var setup = probes[1];
        Assert.True(setup.IsStarting, "AfterAddedToRoom runs while the combat is starting");
        Assert.False(setup.IsInProgress, "IsInProgress turns true only after every AfterCreatureAdded");
        Assert.False(setup.IsEnding);
        Assert.True(setup.IsOverOrEnding);
        Assert.Equal(8m, setup.ModifiedBlock, "the !IsStarting exception lets guarded listeners (Dexterity 3) through");
        Assert.Equal(0m, setup.BlockGained, "CubexConstruct-style GainBlock(13) in AfterAddedToRoom gives no block");
        Assert.True(setup.StrengthApplied);
        Assert.Equal(0, setup.CardsDrawn, "Draw is refused although the draw pile holds the deck");
        Assert.False(setup.GeneratedCardAdded);

        Assert.True(s.IsInProgress);
        Assert.False(s.IsStarting);
        Assert.Equal(0, m.Block, "the setup block never landed");
        Assert.Equal(4, m.GetPowerAmount<StrengthPower>(), "both setup-time Strength applications landed");
        Assert.Equal(3, m.GetPowerAmount<DexterityPower>());
        Assert.Equal(5, s.Player.Hand.Count, "the turn-1 hand draw is unaffected");
        Assert.Equal(5, s.Player.DrawPile.Count);
        Assert.Equal(12, s.NextCardInstanceId, "ten deck cards plus the two refused generated cards");
        Assert.Equal(16m, CreatureCmd.GainBlock(s, m, 13m, ValueProp.Move, null), "the same GainBlock works once the combat is in progress");
        Assert.Equal(16, m.Block);
    }

    public static void Test_Loop_PlayerDeathOutsideAnActiveCombatRecordsNoLoss()
    {
        var cs = CombatLoop.SetUpCombat(61UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var clone = cs.DeepClone();
        CreatureCmd.Kill(clone, clone.Player);
        Assert.Equal(0, clone.Player.CurrentHp);
        Assert.Equal(CombatOutcome.InProgress, clone.Outcome, "Kill calls LoseCombat only while the combat is in progress");

        CombatLoop.StartCombat(cs);
        CreatureCmd.Kill(cs, cs.Player);
        Assert.Equal(CombatOutcome.Defeat, cs.Outcome, "a death in progress records the loss");
        Assert.True(cs.IsInProgress, "the loss stays pending until a win check processes it");
        Assert.True(CombatStatus.IsEnding(cs));
        Assert.True(CombatStatus.IsAboutToLose(cs));
        CombatLoop.CheckWinCondition(cs);
        Assert.False(cs.IsInProgress, "the win check processes the pending loss");
        Assert.False(CombatStatus.IsEnding(cs), "a processed loss is no longer ending");
        Assert.True(CombatStatus.IsOverOrEnding(cs));
        Assert.False(CombatStatus.IsAboutToLose(cs));
        Assert.Equal(CombatOutcome.Defeat, cs.Outcome);
    }

    public static void Test_Loop_ScriptedStrikeFightTurnByTurn()
    {
        var sim = CombatSim.Create(7UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var s = sim.State;
        var p = s.Player;
        var m = s.Monsters[0];

        AssertTurn(s, round: 1, turn: 1, energy: 3, hand: 5, draw: 5, discard: 0);
        Assert.Equal(80, p.CurrentHp);
        Assert.Equal(0, p.Block);
        Assert.Equal(40, m.CurrentHp);
        Assert.SequenceEqual(new[] { GameAction.Play(0, 0), GameAction.Play(1, 0), GameAction.Play(2, 0), GameAction.Play(3, 0), GameAction.Play(4, 0), GameAction.EndTurn }, sim.LegalActions());

        PlayAndCheck(sim, GameAction.Play(0, 0), energy: 2, hand: 4, discard: 1, monsterHp: 34);
        PlayAndCheck(sim, GameAction.Play(0, 0), energy: 1, hand: 3, discard: 2, monsterHp: 28);
        PlayAndCheck(sim, GameAction.Play(0, 0), energy: 0, hand: 2, discard: 3, monsterHp: 22);
        Assert.SequenceEqual(new[] { GameAction.EndTurn }, sim.LegalActions(), "no energy left: only EndTurn");
        Assert.True(LegalActionGenerator.CanPlay(s, p.Hand[0], out var reason) == false && reason == UnplayableReason.EnergyCostTooHigh, $"reason {reason}");

        sim.Step(GameAction.EndTurn);
        AssertTurn(s, round: 2, turn: 2, energy: 3, hand: 5, draw: 0, discard: 5);
        Assert.Equal(22, m.CurrentHp);
        Assert.Equal(80, p.CurrentHp, "the passive dummy does not attack");
        Assert.False(m.SpawnedThisTurn, "the player-to-enemy switch clears the spawned flag");

        PlayAndCheck(sim, GameAction.Play(4, 0), energy: 2, hand: 4, discard: 6, monsterHp: 16);
        PlayAndCheck(sim, GameAction.Play(0, 0), energy: 1, hand: 3, discard: 7, monsterHp: 10);
        PlayAndCheck(sim, GameAction.Play(2, 0), energy: 0, hand: 2, discard: 8, monsterHp: 4);

        sim.Step(GameAction.EndTurn);
        AssertTurn(s, round: 3, turn: 3, energy: 3, hand: 5, draw: 5, discard: 0);
        Assert.Equal(0, p.ExhaustPile.Count);
        Assert.Equal(0, p.PlayPile.Count);

        sim.Step(GameAction.Play(1, 0));
        Assert.Equal(CombatOutcome.Victory, sim.Outcome);
        Assert.True(sim.IsTerminal);
        Assert.Equal(0, m.CurrentHp);
        Assert.Equal(0, s.Monsters.Count, "the killed dummy is removed from combat");
        Assert.Equal(PlayerTurnPhase.None, p.Phase);
        Assert.Equal(2, p.Energy);
        Assert.Equal(4, p.Hand.Count);
        Assert.Equal(1, p.PlayPile.Count, "the killing card stays in the play pile: the move to the discard pile is refused while the combat is ending");
        Assert.Equal(0, p.DiscardPile.Count);
        Assert.Equal(5, p.DrawPile.Count);
        Assert.Equal(3, s.RoundNumber);
        Assert.Equal(0, sim.LegalActions().Count, "no actions after the combat ends");
        Assert.Throws<InvalidOperationException>(() => sim.Step(GameAction.EndTurn));
        Assert.Throws<InvalidOperationException>(() => sim.Step(GameAction.Play(0, 0)));
    }

    public static void Test_Loop_EnemyAttackHitsBlockThenHpAndBlockClearsNextTurn()
    {
        var sim = CombatSim.Create(11UL, Repeat(Defend, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 50, bite: 7)));
        var s = sim.State;
        var p = s.Player;
        var m = s.Monsters[0];
        Assert.Equal(LoopDummy.BiteMoveId, m.Ai.NextMove?.Id);
        Assert.SequenceEqual(new[] { GameAction.Play(0), GameAction.Play(1), GameAction.Play(2), GameAction.Play(3), GameAction.Play(4), GameAction.EndTurn }, sim.LegalActions(), "self-target cards take no target index");

        sim.Step(GameAction.Play(0));
        Assert.Equal(5, p.Block);
        Assert.Equal(2, p.Energy);

        sim.Step(GameAction.EndTurn);
        AssertTurn(s, round: 2, turn: 2, energy: 3, hand: 5, draw: 0, discard: 5);
        Assert.Equal(78, p.CurrentHp, "7 damage, 5 absorbed by block");
        Assert.Equal(0, p.Block, "block is cleared at the start of turn 2");
        Assert.Equal(50, m.CurrentHp);
        Assert.Equal(LoopDummy.BiteMoveId, m.Ai.NextMove?.Id);

        sim.Step(GameAction.Play(0));
        sim.Step(GameAction.Play(0));
        sim.Step(GameAction.Play(0));
        Assert.Equal(15, p.Block);
        Assert.Equal(0, p.Energy);

        sim.Step(GameAction.EndTurn);
        AssertTurn(s, round: 3, turn: 3, energy: 3, hand: 5, draw: 5, discard: 0);
        Assert.Equal(78, p.CurrentHp, "15 block absorbs the 7-damage bite");
        Assert.Equal(0, p.Block, "the remaining 8 block is cleared at turn start");

        sim.Step(GameAction.EndTurn);
        Assert.Equal(71, p.CurrentHp);
        Assert.Equal(4, s.RoundNumber);
        Assert.Equal(CombatOutcome.InProgress, sim.Outcome);
    }

    public static void Test_Loop_PlayerBlockIsNotClearedAtFirstTurnStart()
    {
        var sim = CombatSim.Create(13UL, Repeat(Defend, 10), TestEncounterId, 0, cs =>
        {
            Spawn(new LoopDummy("LOOP_DUMMY_A", 50))(cs);
            cs.Player.Block = 9;
        });
        var p = sim.State.Player;
        Assert.Equal(9, p.Block, "turn 1 keeps block gained before combat start");
        sim.Step(GameAction.EndTurn);
        Assert.Equal(0, p.Block, "turn 2 clears block");
    }

    public static void Test_Loop_EnemiesActInListOrderEachTurn()
    {
        var log = new List<string>();
        var sim = CombatSim.Create(17UL, Repeat(Defend, 10), TestEncounterId, 0, Spawn(
            new LoopDummy("LOOP_DUMMY_A", 30, bite: 3, log: log),
            new LoopDummy("LOOP_DUMMY_B", 30, bite: 4, log: log)));
        Assert.Equal(2, sim.State.Monsters.Count);
        sim.Step(GameAction.EndTurn);
        Assert.SequenceEqual(new[] { "LOOP_DUMMY_A", "LOOP_DUMMY_B" }, log);
        Assert.Equal(73, sim.State.Player.CurrentHp);
        sim.Step(GameAction.EndTurn);
        Assert.SequenceEqual(new[] { "LOOP_DUMMY_A", "LOOP_DUMMY_B", "LOOP_DUMMY_A", "LOOP_DUMMY_B" }, log);
        Assert.Equal(66, sim.State.Player.CurrentHp);
    }

    public static void Test_Loop_PlayerDeathEndsEnemyTurnWithDefeat()
    {
        var log = new List<string>();
        var sim = CombatSim.Create(19UL, Repeat(Defend, 10), TestEncounterId, 0, Spawn(
            new LoopDummy("LOOP_DUMMY_A", 30, bite: 100, log: log),
            new LoopDummy("LOOP_DUMMY_B", 30, bite: 5, log: log)));
        sim.Step(GameAction.EndTurn);
        Assert.Equal(CombatOutcome.Defeat, sim.Outcome);
        Assert.True(sim.IsTerminal);
        Assert.Equal(0, sim.State.Player.CurrentHp);
        Assert.SequenceEqual(new[] { "LOOP_DUMMY_A" }, log, "the win check after the killing move stops the enemy turn");
        Assert.Equal(CombatSide.Enemy, sim.State.CurrentSide);
        Assert.Equal(1, sim.State.RoundNumber);
        Assert.Equal(0, sim.LegalActions().Count);
        Assert.Throws<InvalidOperationException>(() => sim.Step(GameAction.EndTurn));
    }

    public static void Test_Loop_UnaffordableCardIsNotLegal()
    {
        var sim = CombatSim.Create(23UL, Repeat(BashId, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 100)));
        var s = sim.State;
        Assert.Equal(6, sim.LegalActions().Count, "five 2-cost Bashes are affordable with 3 energy, plus EndTurn");
        sim.Step(GameAction.Play(0, 0));
        Assert.Equal(1, s.Player.Energy);
        Assert.Equal(92, s.Monsters[0].CurrentHp);
        Assert.SequenceEqual(new[] { GameAction.EndTurn }, sim.LegalActions());
        Assert.False(LegalActionGenerator.CanPlay(s, s.Player.Hand[0], out var reason));
        Assert.Equal(UnplayableReason.EnergyCostTooHigh, reason);
        Assert.False(sim.IsLegal(GameAction.Play(0, 0)));
        AssertIllegalLeavesStateUnchanged(sim, GameAction.Play(0, 0));
        AssertIllegalLeavesStateUnchanged(sim, GameAction.Play(3, 0));
    }

    public static void Test_Loop_InvalidTargetsAndIndicesThrow()
    {
        var strikes = CombatSim.Create(29UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40), new LoopDummy("LOOP_DUMMY_B", 40)));
        AssertIllegalLeavesStateUnchanged(strikes, GameAction.Play(0));
        AssertIllegalLeavesStateUnchanged(strikes, GameAction.Play(0, 2));
        AssertIllegalLeavesStateUnchanged(strikes, GameAction.Play(0, -2));
        AssertIllegalLeavesStateUnchanged(strikes, GameAction.Play(5, 0));
        AssertIllegalLeavesStateUnchanged(strikes, GameAction.Play(-1, 0));
        AssertIllegalLeavesStateUnchanged(strikes, new GameAction(ActionKind.EndTurn, 0, GameAction.NoIndex));
        AssertIllegalLeavesStateUnchanged(strikes, new GameAction(ActionKind.EndTurn, GameAction.NoIndex, 0));
        AssertIllegalLeavesStateUnchanged(strikes, new GameAction((ActionKind)7, 0, 0));
        Assert.True(strikes.IsLegal(GameAction.Play(0, 1)));
        Assert.True(strikes.IsLegal(GameAction.EndTurn));

        var defends = CombatSim.Create(29UL, Repeat(Defend, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        AssertIllegalLeavesStateUnchanged(defends, GameAction.Play(0, 0));
        Assert.True(defends.IsLegal(GameAction.Play(0)));
    }

    public static void Test_Loop_KilledTargetLeavesTheTargetList()
    {
        var sim = CombatSim.Create(31UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 6), new LoopDummy("LOOP_DUMMY_B", 40)));
        var s = sim.State;
        var first = s.Monsters[0];
        var second = s.Monsters[1];
        Assert.Equal(11, sim.LegalActions().Count, "5 strikes x 2 targets + EndTurn");
        sim.Step(GameAction.Play(0, 0));
        Assert.Equal(0, first.CurrentHp);
        Assert.Equal(CombatOutcome.InProgress, sim.Outcome, "a primary enemy is still alive");
        Assert.Equal(1, s.Monsters.Count);
        Assert.True(ReferenceEquals(second, s.Monsters[0]));
        Assert.SequenceEqual(new[] { GameAction.Play(0, 0), GameAction.Play(1, 0), GameAction.Play(2, 0), GameAction.Play(3, 0), GameAction.EndTurn }, sim.LegalActions());
        AssertIllegalLeavesStateUnchanged(sim, GameAction.Play(0, 1));
        Assert.Equal(1, s.Player.DiscardPile.Count, "the card moves to the discard pile while another enemy lives");
        Assert.Equal(0, s.Player.PlayPile.Count);
    }

    public static void Test_Loop_LegalActionsOrderHandThenTargetThenEndTurn()
    {
        var sim = CombatSim.Create(37UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(
            new LoopDummy("LOOP_DUMMY_A", 40), new LoopDummy("LOOP_DUMMY_B", 40), new LoopDummy("LOOP_DUMMY_C", 40)));
        var expected = new List<GameAction>();
        for (int card = 0; card < 5; card++)
        {
            for (int target = 0; target < 3; target++)
            {
                expected.Add(GameAction.Play(card, target));
            }
        }
        expected.Add(GameAction.EndTurn);
        Assert.SequenceEqual(expected, sim.LegalActions());
        var buffer = new List<GameAction> { GameAction.Play(9, 9) };
        sim.LegalActions(buffer);
        Assert.SequenceEqual(expected, buffer, "the buffer overload clears and refills");
        foreach (var action in expected)
        {
            Assert.True(sim.IsLegal(action), action.ToString());
        }
    }

    public static void Test_Loop_OpeningDrawOrderFollowsShuffleStream()
    {
        const ulong seed = 20261028UL;
        var deck = new[] { Strike, Strike, Strike, Strike, Strike, Defend, Defend, Defend, Defend, BashId };
        var sim = CombatSim.Create(seed, deck, TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 500)));
        var expected = Enumerable.Range(0, 10).ToList();
        GameUnstableShuffle(expected, new RunRngSet(seed).Shuffle);
        var p = sim.State.Player;
        Assert.SequenceEqual(expected.Take(5), p.Hand.Cards.Select(c => c.InstanceId), "hand = top five of the shuffled draw pile, in draw order");
        Assert.SequenceEqual(expected.Skip(5), p.DrawPile.Cards.Select(c => c.InstanceId), "remaining draw pile keeps the shuffled order");
        Assert.Equal(9, sim.State.Rng.Shuffle.Counter, "a 10-card shuffle draws 9 numbers");
    }

    public static void Test_Loop_ReshuffleSortsByCardIdThenContinuesShuffleStream()
    {
        const ulong seed = 424242UL;
        var deck = new[] { Strike, Strike, Strike, Strike, Strike, Defend, Defend, Defend, Defend, Defend };
        var sim = CombatSim.Create(seed, deck, TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 500)));
        var rng = new RunRngSet(seed).Shuffle;
        GameUnstableShuffle(Enumerable.Range(0, 10).ToList(), rng);
        sim.Step(GameAction.EndTurn);
        sim.Step(GameAction.EndTurn);
        var expected = new List<string>();
        expected.AddRange(Repeat(Defend, 5));
        expected.AddRange(Repeat(Strike, 5));
        GameUnstableShuffle(expected, rng);
        var p = sim.State.Player;
        Assert.SequenceEqual(expected.Take(5), p.Hand.Cards.Select(c => c.Id), "turn-3 hand after the reshuffle");
        Assert.SequenceEqual(expected.Skip(5), p.DrawPile.Cards.Select(c => c.Id), "draw pile after the reshuffle");
        Assert.Equal(0, p.DiscardPile.Count);
        Assert.Equal(18, sim.State.Rng.Shuffle.Counter);
    }

    public static void Test_Loop_ShuffleComparerOrdersByIdThenUpgrade()
    {
        var cs = CombatLoop.SetUpCombat(1UL, Array.Empty<string>(), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var strike = CardInstance.Create(cs, Strike);
        var upgraded = CardInstance.Create(cs, Strike);
        upgraded.Upgrade();
        var defend = CardInstance.Create(cs, Defend);
        Assert.True(CardPileCmd.CompareForShuffle(defend, strike) < 0, "ordinal id: DEFEND before STRIKE");
        Assert.True(CardPileCmd.CompareForShuffle(strike, defend) > 0);
        Assert.True(CardPileCmd.CompareForShuffle(strike, upgraded) < 0, "same id: lower upgrade level first");
        Assert.Equal(0, CardPileCmd.CompareForShuffle(strike, strike));
        Assert.Equal(0, CardPileCmd.CompareForShuffle(strike, CardInstance.Create(cs, Strike)), "equal id and level compare equal");
    }

    public static void Test_Loop_PileMovesFollowGameRules()
    {
        var cs = CombatLoop.CreateCombat(3UL, Repeat(Strike, 12), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var p = cs.Player;
        Assert.Equal(5, p.Hand.Count);
        Assert.Equal(7, p.DrawPile.Count);
        var top = p.DrawPile[0];
        CardPileCmd.Add(cs, top, PileType.Discard, CardPilePosition.Top);
        Assert.True(ReferenceEquals(top, p.DiscardPile[0]));
        Assert.Equal(6, p.DrawPile.Count);

        int drawn = CardPileCmd.Draw(cs, 10.5m, p);
        Assert.Equal(5, drawn, "11 draws requested (rounded up) but the hand caps at ten cards");
        Assert.Equal(10, p.Hand.Count);
        Assert.Equal(1, p.DrawPile.Count);
        Assert.Equal(0, CardPileCmd.Draw(cs, 1m, p), "a full hand draws nothing");

        var extra = p.DrawPile[0];
        CardPileCmd.Add(cs, extra, PileType.Hand);
        Assert.True(ReferenceEquals(extra, p.DiscardPile[p.DiscardPile.Count - 1]), "adding to a full hand goes to the bottom of the discard pile");

        var generated = CardInstance.Create(cs, Defend);
        CardPileCmd.AddGeneratedCardToCombat(cs, generated, PileType.Exhaust);
        Assert.True(p.ExhaustPile.Contains(generated));
        Assert.Throws<InvalidOperationException>(() => CardPileCmd.AddGeneratedCardToCombat(cs, generated, PileType.Hand));

        CardPileCmd.RemoveFromCombat(cs, generated);
        Assert.True(p.FindPileContaining(generated) == null);
        Assert.Throws<InvalidOperationException>(() => CardPileCmd.RemoveFromCombat(cs, generated));

        var discarded = p.Hand[0];
        CardPileCmd.Discard(cs, discarded);
        Assert.True(ReferenceEquals(discarded, p.DiscardPile[p.DiscardPile.Count - 1]));
        var exhausted = p.Hand[0];
        CardPileCmd.Exhaust(cs, exhausted);
        Assert.True(ReferenceEquals(exhausted, p.ExhaustPile[p.ExhaustPile.Count - 1]));
        Assert.Equal(8, p.Hand.Count);
    }

    public static void Test_Loop_DrawReshufflesOnlyWhenTheDrawPileIsEmpty()
    {
        var cs = CombatLoop.CreateCombat(41UL, Repeat(Strike, 3), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40)));
        var p = cs.Player;
        Assert.Equal(2, cs.Rng.Shuffle.Counter, "3-card opening shuffle");
        Assert.Equal(3, p.Hand.Count, "the hand draw stops when draw and discard piles are both empty");
        Assert.Equal(0, p.DrawPile.Count);
        CardPileCmd.Add(cs, new List<CardInstance>(p.Hand.Cards), PileType.Discard);
        Assert.Equal(3, p.DiscardPile.Count);
        Assert.Equal(0, p.Hand.Count);
        Assert.Equal(1, CardPileCmd.Draw(cs, 1m, p), "an empty draw pile reshuffles the discard pile first");
        Assert.Equal(4, cs.Rng.Shuffle.Counter, "a 3-card reshuffle draws 2 numbers");
        Assert.Equal(2, p.DrawPile.Count);
        Assert.Equal(0, p.DiscardPile.Count);
        Assert.Equal(1, CardPileCmd.Draw(cs, 1m, p), "a non-empty draw pile is not reshuffled");
        Assert.Equal(4, cs.Rng.Shuffle.Counter);
        Assert.Equal(1, CardPileCmd.Draw(cs, 5m, p), "drawing stops when both piles are empty");
        Assert.Equal(4, cs.Rng.Shuffle.Counter);
        Assert.Equal(3, p.Hand.Count);
        Assert.Equal(0, p.DrawPile.Count);
        Assert.Equal(0, p.DiscardPile.Count);
    }

    public static void Test_Loop_SameSeedAndActionsGiveSameStates()
    {
        var first = RunFirstLegalPolicy(43UL, 40);
        var second = RunFirstLegalPolicy(43UL, 40);
        Assert.SequenceEqual(first.Fingerprints, second.Fingerprints);
        Assert.SequenceEqual(first.Hashes, second.Hashes);
        Assert.Equal(CombatOutcome.Victory, first.Sim.Outcome, "always playing the first legal action kills the dummy");

        var other = CombatSim.Create(44UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 60, bite: 3)));
        Assert.NotEqual(first.Fingerprints[0], Fingerprint(other.State), "a different seed gives a different opening shuffle");
        Assert.NotEqual(first.Hashes[0], other.StateHash());
    }

    public static void Test_Loop_CloneStepsIndependently()
    {
        var sim = CombatSim.Create(47UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40, bite: 6)));
        ulong h0 = sim.StateHash();
        string f0 = Fingerprint(sim.State);
        var scratch = new CanonicalWriter();
        Assert.Equal(h0, sim.StateHash(scratch), "the scratch-writer hash equals the allocating hash");
        var clone = sim.Clone();
        Assert.Equal(h0, clone.StateHash());
        Assert.Equal(h0, clone.StateHash(scratch), "a reused scratch writer is reset before hashing");
        Assert.Equal(f0, Fingerprint(clone.State));

        clone.Step(GameAction.Play(0, 0));
        clone.Step(GameAction.EndTurn);
        Assert.Equal(h0, sim.StateHash(), "stepping the clone must not touch the original");
        Assert.Equal(f0, Fingerprint(sim.State));
        Assert.NotEqual(h0, clone.StateHash());

        sim.Step(GameAction.Play(0, 0));
        sim.Step(GameAction.EndTurn);
        Assert.Equal(clone.StateHash(), sim.StateHash(), "the same actions from equal states give equal states");
        Assert.Equal(Fingerprint(clone.State), Fingerprint(sim.State));
        Assert.Equal(74, sim.State.Player.CurrentHp);
    }

    public static void Test_Loop_EndPlayerTurnAndRunEnemyTurnBuildingBlocks()
    {
        var log = new List<string>();
        var cs = CombatLoop.CreateCombat(53UL, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 40, bite: 2, log: log)));
        var p = cs.Player;
        CombatLoop.EndPlayerTurn(cs);
        Assert.Equal(CombatSide.Enemy, cs.CurrentSide);
        Assert.Equal(PlayerTurnPhase.End, p.Phase);
        Assert.Equal(0, p.Hand.Count);
        Assert.Equal(5, p.DiscardPile.Count);
        Assert.Equal(0, log.Count);
        Assert.Throws<InvalidOperationException>(() => CombatLoop.StartPlayerTurn(cs));

        CombatLoop.RunEnemyTurn(cs);
        Assert.SequenceEqual(new[] { "LOOP_DUMMY_A" }, log);
        Assert.Equal(78, p.CurrentHp);
        Assert.Equal(CombatSide.Player, cs.CurrentSide);
        Assert.Equal(2, cs.RoundNumber);
        Assert.Equal(2, p.TurnNumber);
        Assert.Equal(PlayerTurnPhase.None, p.Phase, "the enemy turn start resets the player phase");

        CombatLoop.StartPlayerTurn(cs);
        Assert.Equal(PlayerTurnPhase.Play, p.Phase);
        Assert.Equal(3, p.Energy);
        Assert.Equal(5, p.Hand.Count);
    }

    private static (CombatSim Sim, List<string> Fingerprints, List<ulong> Hashes) RunFirstLegalPolicy(ulong seed, int maxSteps)
    {
        var sim = CombatSim.Create(seed, Repeat(Strike, 10), TestEncounterId, 0, Spawn(new LoopDummy("LOOP_DUMMY_A", 60, bite: 3)));
        var fingerprints = new List<string> { Fingerprint(sim.State) };
        var hashes = new List<ulong> { sim.StateHash() };
        var buffer = new List<GameAction>(16);
        for (int step = 0; step < maxSteps && !sim.IsTerminal; step++)
        {
            sim.LegalActions(buffer);
            Assert.True(buffer.Count > 0, "a combat in progress offers at least EndTurn");
            Assert.Equal(GameAction.EndTurn, buffer[buffer.Count - 1], "EndTurn is always last");
            sim.Step(buffer[0]);
            fingerprints.Add(Fingerprint(sim.State));
            hashes.Add(sim.StateHash());
        }
        return (sim, fingerprints, hashes);
    }

    private static void PlayAndCheck(CombatSim sim, GameAction action, int energy, int hand, int discard, int monsterHp)
    {
        var s = sim.State;
        var monster = s.Monsters[action.TargetIndex];
        sim.Step(action);
        Assert.Equal(energy, s.Player.Energy, $"energy after {action}");
        Assert.Equal(hand, s.Player.Hand.Count, $"hand after {action}");
        Assert.Equal(discard, s.Player.DiscardPile.Count, $"discard after {action}");
        Assert.Equal(monsterHp, monster.CurrentHp, $"monster hp after {action}");
        Assert.Equal(0, s.Player.PlayPile.Count, $"play pile after {action}");
        Assert.Equal(PlayerTurnPhase.Play, s.Player.Phase);
    }

    private static void AssertTurn(CombatState s, int round, int turn, int energy, int hand, int draw, int discard)
    {
        Assert.Equal(CombatOutcome.InProgress, s.Outcome, $"outcome in round {round}");
        Assert.Equal(CombatSide.Player, s.CurrentSide, $"side in round {round}");
        Assert.Equal(PlayerTurnPhase.Play, s.Player.Phase, $"phase in round {round}");
        Assert.Equal(round, s.RoundNumber, "round number");
        Assert.Equal(turn, s.Player.TurnNumber, "turn number");
        Assert.Equal(energy, s.Player.Energy, $"energy at the start of round {round}");
        Assert.Equal(hand, s.Player.Hand.Count, $"hand size at the start of round {round}");
        Assert.Equal(draw, s.Player.DrawPile.Count, $"draw pile at the start of round {round}");
        Assert.Equal(discard, s.Player.DiscardPile.Count, $"discard pile at the start of round {round}");
    }

    private static void AssertIllegalLeavesStateUnchanged(CombatSim sim, GameAction action)
    {
        string before = Fingerprint(sim.State);
        ulong hashBefore = sim.StateHash();
        Assert.False(sim.IsLegal(action), $"{action} must be illegal");
        Assert.False(sim.LegalActions().Contains(action), $"{action} must not be listed");
        Assert.Throws<InvalidOperationException>(() => sim.Step(action), action.ToString());
        Assert.Equal(before, Fingerprint(sim.State), $"state after rejecting {action}");
        Assert.Equal(hashBefore, sim.StateHash(), $"hash after rejecting {action}");
    }

    private static void GameUnstableShuffle<T>(List<T> list, Rng rng)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }

    private static string[] Repeat(string id, int count) => Enumerable.Repeat(id, count).ToArray();

    private static string Fingerprint(CombatState s)
    {
        var sb = new StringBuilder(256);
        sb.Append(s.Outcome).Append('|').Append(s.RoundNumber).Append('|').Append(s.CurrentSide).Append('|');
        var p = s.Player;
        sb.Append(p.CurrentHp).Append('/').Append(p.MaxHp).Append(" b").Append(p.Block).Append(" e").Append(p.Energy)
            .Append(" s").Append(p.Stars).Append(" t").Append(p.TurnNumber).Append(' ').Append(p.Phase).Append('|');
        foreach (var power in p.Powers)
        {
            sb.Append(power.Id).Append(power.Amount).Append(',');
        }
        foreach (var pile in p.AllPiles)
        {
            sb.Append(pile.Type).Append(':');
            foreach (var card in pile.Cards)
            {
                sb.Append(card.Id).Append('#').Append(card.InstanceId).Append(card.CurrentTargetId).Append(',');
            }
            sb.Append('|');
        }
        foreach (var m in s.Monsters)
        {
            sb.Append(m.CombatId).Append(':').Append(m.CurrentHp).Append('/').Append(m.MaxHp).Append(" b").Append(m.Block)
                .Append(' ').Append(m.SpawnedThisTurn).Append(' ').Append(m.HasAi ? m.Ai.NextMove?.Id : "-");
            foreach (var power in m.Powers)
            {
                sb.Append(power.Id).Append(power.Amount).Append(',');
            }
            sb.Append(';');
        }
        sb.Append('|').Append(s.Rng.Shuffle.Counter).Append(',').Append(s.Rng.MonsterAi.Counter).Append(',').Append(s.NextCardInstanceId);
        return sb.ToString();
    }

    private static Action<CombatState> Spawn(params MonsterDef[] defs) => cs =>
    {
        foreach (var def in defs)
        {
            int hp = def.MaxInitialHp(cs);
            var monster = new MonsterState(cs.AllocateCreatureId(), def, hp, hp, null, null);
            cs.Monsters.Add(monster);
        }
    };

    private sealed record SetupWindowProbe(
        bool IsStarting,
        bool IsInProgress,
        bool IsEnding,
        bool IsOverOrEnding,
        decimal ModifiedBlock,
        decimal BlockGained,
        bool StrengthApplied,
        int CardsDrawn,
        bool GeneratedCardAdded);

    private static SetupWindowProbe ProbeSetupWindow(CombatState cs, MonsterState monster)
    {
        bool isStarting = cs.IsStarting;
        bool isInProgress = cs.IsInProgress;
        bool isEnding = CombatStatus.IsEnding(cs);
        bool isOverOrEnding = CombatStatus.IsOverOrEnding(cs);
        decimal modifiedBlock = Hook.ModifyBlock(cs, monster, 5m, ValueProp.Move, null, null);
        decimal blockGained = CreatureCmd.GainBlock(cs, monster, 13m, ValueProp.Move, null);
        bool strengthApplied = PowerCmd.Apply<StrengthPower>(cs, monster, 2m, monster, null) != null;
        int drawn = CardPileCmd.Draw(cs, 1m, cs.Player);
        var generated = CardInstance.Create(cs, Strike);
        CardPileCmd.AddGeneratedCardToCombat(cs, generated, PileType.Hand);
        bool added = cs.Player.FindPileContaining(generated) != null;
        return new SetupWindowProbe(isStarting, isInProgress, isEnding, isOverOrEnding, modifiedBlock, blockGained, strengthApplied, drawn, added);
    }

    private sealed class SetupProbeMonster : MonsterDef
    {
        private readonly int _hp;
        private readonly List<SetupWindowProbe> _probes;

        public SetupProbeMonster(string id, int hp, List<SetupWindowProbe> probes)
            : base(id)
        {
            _hp = hp;
            _probes = probes;
        }

        public override int MinInitialHp(CombatState cs) => _hp;

        public override int MaxInitialHp(CombatState cs) => _hp;

        public override void AfterAddedToRoom(CombatState cs, MonsterState self) => _probes.Add(ProbeSetupWindow(cs, self));

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var wait = new MoveStateDef(LoopDummy.WaitMoveId, static (_, _, _) => { }) { FollowUpStateId = LoopDummy.WaitMoveId };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
        }
    }

    private sealed class LoopDummy : MonsterDef
    {
        public const string WaitMoveId = "LOOP_WAIT";
        public const string BiteMoveId = "LOOP_BITE";

        private readonly int _hp;
        private readonly int _bite;
        private readonly List<string>? _log;

        public LoopDummy(string id, int hp, int bite = 0, List<string>? log = null)
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
