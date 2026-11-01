using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_9Tests
{
    private static readonly string[] PortedIds =
    {
        "ADAPTABLE_POWER", "ASLEEP_POWER", "BACK_ATTACK_LEFT_POWER", "BACK_ATTACK_RIGHT_POWER",
        "BATTLEWORN_DUMMY_TIME_LIMIT_POWER", "BURROWED_POWER", "CRAB_RAGE_POWER", "CURL_UP_POWER",
        "DAMPEN_POWER", "ENRAGE_POWER", "ESCAPE_ARTIST_POWER", "FLUTTER_POWER", "HARDENED_SHELL_POWER",
        "HARD_TO_KILL_POWER", "HATCH_POWER", "HIGH_VOLTAGE_POWER", "ILLUSION_POWER", "IMBALANCED_POWER",
        "NEMESIS_POWER", "PAINFUL_STABS_POWER", "PAPER_CUTS_POWER", "PERSONAL_HIVE_POWER", "PLOW_POWER",
        "POSSESS_SPEED_POWER", "POSSESS_STRENGTH_POWER", "RAMPART_POWER", "REATTACH_POWER", "SANDPIT_POWER",
        "SHRIEK_POWER", "SKITTISH_POWER", "SLOW_POWER", "SLUMBER_POWER", "SOAR_POWER", "STEAM_ERUPTION_POWER",
        "SUCK_POWER", "SURROUNDED_POWER", "TENDER_POWER", "TERRITORIAL_POWER", "WITHERING_PRESENCE_POWER",
    };

    private sealed class Powers9MonsterDouble : MonsterDef
    {
        public Powers9MonsterDouble()
            : base("POWERS_9_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat(int monsters = 1)
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3) { Energy = 3 };
        var cs = new CombatState(player, "POWERS_9_TEST", 0, new RunRngSet(new Rng(909UL)));
        cs.IsInProgress = true;
        var def = new Powers9MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, 50, 50, null, null));
        }
        return cs;
    }

    public static void Test_Content_powers_9_AllIdsRegistered()
    {
        var ids = new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in PortedIds)
        {
            Assert.True(ids.Contains(id), $"{id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id, $"{id} round-trips through the registry");
        }
    }

    public static void Test_Content_powers_9_HardenedShellCapsHpLossPerTurn()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        var shell = new HardenedShellPower();
        shell.ApplyInternal(monster, 10);
        Assert.Equal(10m, shell.ModifyHpLostBeforeOstyLate(cs.Run, monster, 15m, ValueProp.Move, cs.Player, null), "capped at Amount");
        var result = new DamageResult(monster, ValueProp.Move) { UnblockedDamage = 10 };
        shell.AfterDamageReceived(cs.Run, monster, result, ValueProp.Move, cs.Player, null);
        Assert.Equal(0m, shell.ModifyHpLostBeforeOstyLate(cs.Run, monster, 5m, ValueProp.Move, cs.Player, null), "cap exhausted this turn");
        shell.BeforeSideTurnStart(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(5m, shell.ModifyHpLostBeforeOstyLate(cs.Run, monster, 5m, ValueProp.Move, cs.Player, null), "reset at side turn start");
    }

    public static void Test_Content_powers_9_SurroundedBackAttackMultiplier()
    {
        var cs = NewCombat(2);
        var left = cs.Monsters[0];
        var right = cs.Monsters[1];
        new BackAttackLeftPower().ApplyInternal(left, 1);
        new BackAttackRightPower().ApplyInternal(right, 1);
        var surrounded = new SurroundedPower();
        surrounded.ApplyInternal(cs.Player, 1);
        Assert.Equal(1.5m, surrounded.ModifyDamageMultiplicative(cs.Run, cs.Player, 10m, ValueProp.Move, left, null, null), "facing right, the back-attack-left crab hits from behind");
        Assert.Equal(1m, surrounded.ModifyDamageMultiplicative(cs.Run, cs.Player, 10m, ValueProp.Move, right, null, null), "facing right, the right crab is in front");
    }

    public static void Test_Content_powers_9_NemesisTogglesIntangible()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        var nemesis = new NemesisPower();
        nemesis.ApplyInternal(monster, 1);
        var participants = new CreatureState[] { monster };
        nemesis.AfterSideTurnEnd(cs, CombatSide.Enemy, participants);
        Assert.True(monster.HasPower<IntangiblePower>(), "first owner turn end applies Intangible");
        nemesis.AfterSideTurnEnd(cs, CombatSide.Enemy, participants);
        Assert.True(!monster.HasPower<IntangiblePower>(), "second owner turn end removes it");
    }
}
