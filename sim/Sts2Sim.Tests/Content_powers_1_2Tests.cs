using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_1_2Tests
{
    private static readonly string[] PortedIds =
    {
        "INTANGIBLE_POWER", "JUGGERNAUT_POWER", "MACHINE_LEARNING_POWER", "MIND_ROT_POWER", "MINION_POWER",
        "NO_BLOCK_POWER", "ORBIT_POWER", "PAGESTORM_POWER", "PILLAR_OF_CREATION_POWER", "PYRE_POWER",
        "RADIANCE_POWER", "RUPTURE_POWER", "SELF_FORMING_CLAY_POWER", "SERPENT_FORM_POWER", "SIGNAL_BOOST_POWER",
        "SLIPPERY_POWER", "SMOKESTACK_POWER", "SPEEDSTER_POWER", "SPIRIT_OF_ASH_POWER", "SUBROUTINE_POWER",
        "TAINTED_POWER", "THE_GAMBIT_POWER", "THE_HUNT_POWER", "VEILPIERCER_POWER", "VICIOUS_POWER",
        "VIGOR_POWER", "WASTE_AWAY_POWER", "WELL_LAID_PLANS_POWER",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("CONTENT_POWERS_1_2_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class SkillDouble : CardDef
    {
        public SkillDouble()
            : base("CONTENT_POWERS_1_2_TEST_SKILL", 1, CardType.Skill, CardRarity.Basic, TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var cs = new CombatState(player, "CONTENT_POWERS_1_2_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    public static void Test_ContentPowers12_EveryPortedIdIsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.Equal(id, PowerRegistry.Create(id).Id);
        }
    }

    public static void Test_ContentPowers12_OrbitGainsEnergyPerFourSpent()
    {
        var cs = NewCombat();
        var orbit = Assert.NotNull(PowerCmd.Apply<OrbitPower>(cs, cs.Player, 2m, cs.Player, null));
        var card = new CardInstance(new SkillDouble(), 300);
        cs.Player.Energy = 0;
        orbit.AfterEnergySpent(cs, card, 3);
        Assert.Equal(0, cs.Player.Energy, "3 spent is below the increment");
        orbit.AfterEnergySpent(cs, card, 5);
        Assert.Equal(4, cs.Player.Energy, "8 spent = 2 triggers x Amount 2");
        Assert.Equal(2, orbit.TriggerCount);
        var clone = (OrbitPower)orbit.CloneFor(cs.Player);
        Assert.Equal(8, clone.EnergySpent);
    }

    public static void Test_ContentPowers12_RuptureDefersStrengthFromSelfDamageCardsToAfterPlay()
    {
        var cs = NewCombat();
        var rupture = Assert.NotNull(PowerCmd.Apply<RupturePower>(cs, cs.Player, 1m, cs.Player, null));
        var card = new CardInstance(new SkillDouble(), 301);
        var play = new CardPlay(cs, card, null);
        rupture.BeforeCardPlayed(cs, play);
        CreatureCmd.Damage(cs, cs.Player, 3m, ValueProp.Unblockable | ValueProp.Unpowered, cs.Player, card, play);
        Assert.Equal(0, cs.Player.GetPowerAmount<StrengthPower>(), "strength from a card's self-damage waits for the play to finish");
        rupture.AfterCardPlayed(cs, play);
        Assert.Equal(1, cs.Player.GetPowerAmount<StrengthPower>());
        CreatureCmd.Damage(cs, cs.Player, 2m, ValueProp.Unblockable | ValueProp.Unpowered, cs.Player);
        Assert.Equal(2, cs.Player.GetPowerAmount<StrengthPower>(), "damage without a tracked card applies immediately");
    }
}
