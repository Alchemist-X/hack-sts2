using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_3457Tests
{
    private static readonly string[] PortedIds =
    {
        "AGGRESSION_POWER", "CORRUPTION_POWER", "INFINITE_BLADES_POWER", "MASTER_PLANNER_POWER", "MAYHEM_POWER",
        "NIGHTMARE_POWER", "REBOUND_POWER", "SENTRY_MODE_POWER", "STAMPEDE_POWER", "ECHO_FORM_POWER", "FERAL_POWER",
        "ITERATION_POWER", "JUGGLING_POWER", "LETHALITY_POWER", "NOSTALGIA_POWER", "PALE_BLUE_DOT_POWER",
        "PHANTOM_BLADES_POWER", "UNMOVABLE_POWER", "CALAMITY_POWER", "CALL_OF_THE_VOID_POWER", "CHAINS_OF_BINDING_POWER",
        "CREATIVE_AI_POWER", "GALVANIC_POWER", "HELLO_WORLD_POWER", "HEX_POWER", "RINGING_POWER", "SMOGGY_POWER",
        "SPECTRUM_SHIFT_POWER", "TANGLED_POWER", "VITAL_SPARK_POWER", "FOREGONE_CONCLUSION_POWER", "STRATAGEM_POWER",
        "TYRANNY_POWER", "INFESTED_POWER", "STOCK_POWER",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("P3457_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class AttackDouble : CardDef
    {
        public AttackDouble()
            : base("P3457_TEST_ATTACK", 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var cs = new CombatState(player, "P3457_TEST", 0, new RunRngSet(new Rng(3457UL)));
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static CardInstance OwnedAttack(CombatState cs, int instanceId)
    {
        var card = new CardInstance(new AttackDouble(), instanceId);
        card.AttachTo(cs.Player);
        return card;
    }

    public static void Test_Content_powers_3457_AllPortedIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.Equal(id, PowerRegistry.Create(id).Id);
        }
        Assert.Equal(PowerInstanceType.Instanced, PowerRegistry.Create("NIGHTMARE_POWER").InstanceType);
        Assert.Equal(PowerType.Debuff, PowerRegistry.Create("TANGLED_POWER").Type);
        Assert.Equal(PowerStackType.Single, PowerRegistry.Create("HEX_POWER").StackType);
    }

    public static void Test_Content_powers_3457_JugglingClonesTheThirdAttack()
    {
        var cs = NewCombat();
        var juggling = Assert.NotNull(PowerCmd.Apply<JugglingPower>(cs, cs.Player, 1m, cs.Player, null));
        for (int i = 0; i < 3; i++)
        {
            var card = OwnedAttack(cs, 900 + i);
            cs.Player.PlayPile.AddInternal(card);
            juggling.BeforeCardPlayed(cs, new CardPlay(cs, card, cs.Monsters[0]));
            Assert.Equal(i == 2 ? 1 : 0, cs.Player.Hand.Count, "only the third Attack is cloned");
        }
        Assert.Equal("P3457_TEST_ATTACK", cs.Player.Hand[0].Id);
        Assert.Equal(3, juggling.AttacksPlayedThisTurn);
        juggling.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(0, juggling.AttacksPlayedThisTurn);
    }

    public static void Test_Content_powers_3457_UnmovableDoublesFirstCardBlockOnly()
    {
        var cs = NewCombat();
        var unmovable = Assert.NotNull(PowerCmd.Apply<UnmovablePower>(cs, cs.Player, 1m, cs.Player, null));
        var card = OwnedAttack(cs, 950);
        var play = new CardPlay(cs, card, null);
        Assert.Equal(2m, unmovable.ModifyBlockMultiplicative(cs, cs.Player, 5m, ValueProp.Move, card, play));
        Assert.Equal(1m, unmovable.ModifyBlockMultiplicative(cs, cs.Player, 5m, ValueProp.Unpowered, card, play), "non-move block is not doubled");
        Assert.Equal(1m, unmovable.ModifyBlockMultiplicative(cs, cs.Monsters[0], 5m, ValueProp.Move, null, null), "monsters are ignored");
        cs.History.AppendBlockGained(cs, cs.Player, 10, ValueProp.Move, play);
        Assert.Equal(2m, unmovable.ModifyBlockMultiplicative(cs, cs.Player, 5m, ValueProp.Move, card, play), "the current play is excluded");
        var next = new CardPlay(cs, OwnedAttack(cs, 951), null);
        Assert.Equal(1m, unmovable.ModifyBlockMultiplicative(cs, cs.Player, 5m, ValueProp.Move, next.Card, next));
    }

    public static void Test_Content_powers_3457_EchoFormAddsOnePlayForFirstCards()
    {
        var cs = NewCombat();
        var echo = Assert.NotNull(PowerCmd.Apply<EchoFormPower>(cs, cs.Player, 1m, cs.Player, null));
        var card = OwnedAttack(cs, 960);
        Assert.Equal(2, echo.ModifyCardPlayCount(cs, card, null, 1));
        cs.History.AppendCardPlayStarted(cs, new CardPlay(cs, card, null));
        Assert.Equal(1, echo.ModifyCardPlayCount(cs, card, null, 1), "one first-in-series play this turn uses up Amount 1");
    }
}
