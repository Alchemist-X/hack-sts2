using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_1_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ACCELERANT_POWER", "ACCURACY_POWER", "AFTERIMAGE_POWER", "AMBERGRIS_POWER", "ARSENAL_POWER",
        "AUTOMATION_POWER", "BARRICADE_POWER", "BLOCK_NEXT_TURN_POWER", "BUFFER_POWER", "COLOSSUS_POWER",
        "CONFUSED_POWER", "CRIMSON_MANTLE_POWER", "CRUELTY_POWER", "CURIOUS_POWER", "DANSE_MACABRE_POWER",
        "DEMESNE_POWER", "ENERGY_NEXT_TURN_POWER", "FAN_OF_KNIVES_POWER", "FASTEN_POWER", "FEEL_NO_PAIN_POWER",
        "FLAME_BARRIER_POWER", "FREE_ATTACK_POWER", "FREE_POWER_POWER", "FREE_SKILL_POWER", "FRIENDSHIP_POWER",
        "GIGANTIFICATION_POWER", "HANG_POWER", "HAUNT_POWER", "INFERNO_POWER",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("POWERS_1_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class AttackDouble : CardDef
    {
        public AttackDouble()
            : base("POWERS_1_1_TEST_ATTACK", 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy, false)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var cs = new CombatState(player, "POWERS_1_1_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        return cs;
    }

    public static void Test_Content_powers_1_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var power = PowerRegistry.Create(id);
            Assert.Equal(id, power.Id);
        }
    }

    public static void Test_Content_powers_1_1_BufferPreventsHpLossAndDecrements()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        Assert.NotNull(PowerCmd.Apply<BufferPower>(cs, cs.Player, 2, cs.Player, null));
        DamageCmd.Attack(cs, 10m).FromMonster(monster).WithHitCount(1).Execute();
        Assert.Equal(80, cs.Player.CurrentHp);
        Assert.Equal(1, cs.Player.GetPowerAmount<BufferPower>());
    }

    public static void Test_Content_powers_1_1_GigantificationTriplesOneAttackThenDecrements()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        Assert.NotNull(PowerCmd.Apply<GigantificationPower>(cs, cs.Player, 1, cs.Player, null));
        var card = new CardInstance(new AttackDouble(), 100);
        var play = new CardPlay(cs, card, monster);
        DamageCmd.Attack(cs, 5m).FromCard(card, play).Targeting(monster).WithHitCount(1).Execute();
        Assert.Equal(35, monster.CurrentHp);
        Assert.False(cs.Player.HasPower<GigantificationPower>());
    }

    public static void Test_Content_powers_1_1_FlameBarrierRetaliatesOnPoweredAttack()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        Assert.NotNull(PowerCmd.Apply<FlameBarrierPower>(cs, cs.Player, 4, cs.Player, null));
        DamageCmd.Attack(cs, 3m).FromMonster(monster).WithHitCount(2).Execute();
        Assert.Equal(42, monster.CurrentHp);
        Assert.Equal(74, cs.Player.CurrentHp);
    }
}
