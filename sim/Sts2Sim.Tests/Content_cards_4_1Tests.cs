using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_4_1Tests
{
    private static readonly string[] PortedIds =
    {
        "BANSHEES_CRY", "BOLAS", "DEATHS_DOOR", "DEATH_MARCH", "ECHO_FORM", "EVIL_EYE", "FERAL", "FINISHER", "FTL", "GOLD_AXE",
        "HELIX_DRILL", "ITERATION", "JUGGLING", "LETHALITY", "LUNAR_BLAST", "MAKE_IT_SO", "MEMENTO_MORI", "MURDER", "NORMALITY", "NOSTALGIA",
        "PALE_BLUE_DOT", "PHANTOM_BLADES", "PINPOINT", "PULL_FROM_BELOW", "SPITE", "STOMP", "SUPERMASSIVE", "TEAR_ASUNDER", "THRUMMING_HATCHET", "UNMOVABLE"
    };

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Content_cards_4_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.Equal(id, def.Id);
            Assert.False(def is UnportedCardDef, id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name));
            var card = new CardInstance(def, 1);
            Assert.NotNull(card.Vars);
        }
    }

    public static void Test_Content_cards_4_1_StompCostDropsPerAttackPlayed()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var stomp = InHand(cs, Stomp.CardId);
        var strike = InHand(cs, StrikeIronclad.CardId);
        var defend = InHand(cs, DefendIronclad.CardId);
        var listener = Assert.NotNull(stomp.Listener);
        listener.BeforeCardPlayed(cs, new CardPlay(cs, defend, null));
        Assert.Equal(3, stomp.EnergyCost.GetWithModifiers(CostModifiers.All), "a Skill leaves Stomp's cost alone");
        listener.BeforeCardPlayed(cs, new CardPlay(cs, strike, null));
        Assert.Equal(2, stomp.EnergyCost.GetWithModifiers(CostModifiers.All), "each Attack lowers Stomp by one this turn");
        stomp.EnergyCost.EndOfTurnCleanup();
        Assert.Equal(3, stomp.EnergyCost.GetWithModifiers(CostModifiers.All));
    }

    public static void Test_Content_cards_4_1_NormalityBlocksTheFourthPlay()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var normality = InHand(cs, Normality.CardId);
        var strike = InHand(cs, StrikeIronclad.CardId);
        var listener = Assert.NotNull(normality.Listener);
        for (int i = 0; i < Normality.NumOfCardsPerTurn; i++)
        {
            Assert.True(listener.ShouldPlay(cs, strike, AutoPlayType.None), "plays below the limit are allowed");
            cs.History.AppendCardPlayStarted(cs, new CardPlay(cs, strike, null));
        }
        Assert.False(listener.ShouldPlay(cs, strike, AutoPlayType.None), "the fourth play this turn is prevented");
        Assert.Equal(0m, ((CalculatedVar)normality.Vars[Normality.CalculatedCardsKey]).Calculate(cs, normality, null));
    }

    public static void Test_Content_cards_4_1_GoldAxeScalesWithFinishedPlays()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 40);
        var goldAxe = InHand(cs, GoldAxe.CardId);
        var strike = InHand(cs, StrikeIronclad.CardId);
        cs.History.AppendCardPlayFinished(cs, new CardPlay(cs, strike, monster));
        cs.History.AppendCardPlayFinished(cs, new CardPlay(cs, strike, monster));
        goldAxe.OnPlay(cs, new CardPlay(cs, goldAxe, monster));
        Assert.Equal(38, monster.CurrentHp);
    }

    private static CardInstance InHand(CombatState cs, string id)
    {
        var card = CardInstance.Create(cs, id);
        card.AttachTo(cs.Player);
        cs.Player.Hand.AddInternal(card);
        return card;
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = 3;
        var cs = new CombatState(player, "CONTENT_CARDS_4_1", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_4_1_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_4_1_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
