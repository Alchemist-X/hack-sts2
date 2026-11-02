using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_2_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ANTICIPATE_POWER", "ARTIFACT_POWER", "BLUR_POWER", "BORROWED_TIME_POWER", "BURST_POWER",
        "CLARITY_POWER", "CONSTRICT_POWER", "CORROSIVE_WAVE_POWER", "COUNTDOWN_POWER", "CRUSH_UNDER_POWER",
        "DARK_EMBRACE_POWER", "DARK_SHACKLES_POWER", "DEBILITATE_POWER", "DEMISE_POWER", "DEMON_FORM_POWER",
        "DISINTEGRATION_POWER", "DOOM_POWER", "DOUBLE_DAMAGE_POWER", "DRAW_CARDS_NEXT_TURN_POWER",
        "DUPLICATION_POWER", "DYING_STAR_POWER", "ENFEEBLING_TOUCH_POWER", "ENVENOM_POWER",
        "FEEDING_FRENZY_POWER", "FLEX_POTION_POWER", "HELICAL_DART_POWER", "MANGLE_POWER",
        "MONARCHS_GAZE_POWER", "MONARCHS_GAZE_STRENGTH_DOWN_POWER", "MONOLOGUE_POWER", "NEUROSURGE_POWER",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("POWERS_2_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat(int monsters, int monsterHp = 50)
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var cs = new CombatState(player, "POWERS_2_1_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(i + 1, def, monsterHp, monsterHp, null, null));
        }
        cs.NextCreatureId = monsters + 1;
        return cs;
    }

    public static void Test_Content_powers_2_1_AllIdsRegistered()
    {
        var registered = new HashSet<string>(PowerRegistry.AllIds);
        foreach (var id in PortedIds)
        {
            Assert.True(registered.Contains(id), $"{id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_powers_2_1_ArtifactNegatesDebuffAndDecrements()
    {
        var cs = NewCombat(1);
        var monster = cs.Monsters[0];
        PowerCmd.Apply<ArtifactPower>(cs, monster, 2m, monster, null);
        PowerCmd.Apply<VulnerablePower>(cs, monster, 2m, cs.Player, null);
        Assert.Null(monster.GetPower<VulnerablePower>(), "Artifact blocks the debuff");
        Assert.Equal(1, monster.GetPowerAmount<ArtifactPower>());
        PowerCmd.Apply<StrengthPower>(cs, monster, 3m, monster, null);
        Assert.Equal(3, monster.GetPowerAmount<StrengthPower>(), "buffs pass through");
        Assert.Equal(1, monster.GetPowerAmount<ArtifactPower>());
        PowerCmd.Apply<VulnerablePower>(cs, monster, 1m, cs.Player, null);
        Assert.Null(monster.GetPower<ArtifactPower>(), "the last charge is consumed");
        Assert.Null(monster.GetPower<VulnerablePower>());
    }

    public static void Test_Content_powers_2_1_DoomKillsEnemiesAtTheirTurnEnd()
    {
        var cs = NewCombat(2, monsterHp: 10);
        var doomed = cs.Monsters[0];
        var safe = cs.Monsters[1];
        PowerCmd.Apply<DoomPower>(cs, doomed, 10m, cs.Player, null);
        PowerCmd.Apply<DoomPower>(cs, safe, 9m, cs.Player, null);
        var participants = new CreatureState[] { doomed, safe };
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, participants);
        Assert.True(doomed.IsAlive, "enemy doom resolves before the enemy turn ends, not after");
        Hook.BeforeSideTurnEnd(cs, CombatSide.Enemy, participants);
        Assert.True(doomed.IsDead, "HP <= Doom kills");
        Assert.True(safe.IsAlive, "HP > Doom survives");
    }
}
