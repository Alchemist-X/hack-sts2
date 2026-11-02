using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_2_2Tests
{
    private static readonly string[] PortedIds =
    {
        "NOXIOUS_FUMES_POWER", "NO_DRAW_POWER", "OBLIVION_POWER", "ONE_TWO_PUNCH_POWER", "PANACHE_POWER",
        "PIERCING_WAIL_POWER", "PLATING_POWER", "POISON_POWER", "PREP_TIME_POWER", "RAGE_POWER",
        "REAPER_FORM_POWER", "REFLECT_POWER", "REGEN_POWER", "REPTILE_TRINKET_POWER", "RETAIN_HAND_POWER",
        "RITUAL_POWER", "SETUP_STRIKE_POWER", "SHACKLING_POTION_POWER", "SHADOWMELD_POWER", "SHADOW_STEP_POWER",
        "SHROUD_POWER", "SLEIGHT_OF_FLESH_POWER", "SLOTH_POWER", "SPEED_POTION_POWER", "STRANGLE_POWER",
        "THE_BOMB_POWER", "TORIC_TOUGHNESS_POWER", "TRACKING_POWER", "WRAITH_FORM_POWER",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("CONTENT_P22_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class SkillDouble : CardDef
    {
        public SkillDouble()
            : base("CONTENT_P22_TEST_SKILL", 1, CardType.Skill, CardRarity.Basic, TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat(int monsters = 2)
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var cs = new CombatState(player, "CONTENT_P22_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(i + 1, def, 50, 50, null, null));
        }
        cs.NextCreatureId = monsters + 1;
        return cs;
    }

    public static void Test_Content_powers_2_2_AllIdsRegistered()
    {
        var all = new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in PortedIds)
        {
            Assert.True(all.Contains(id), $"power {id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_powers_2_2_PoisonLosesHpThenDecrements()
    {
        var cs = NewCombat(1);
        var m = cs.Monsters[0];
        var poison = Assert.NotNull(PowerCmd.Apply<PoisonPower>(cs, m, 3m, cs.Player, null));
        poison.Trigger(cs);
        Assert.Equal(47, m.CurrentHp);
        Assert.Equal(2, poison.Amount);
    }

    public static void Test_Content_powers_2_2_StrangleHitsAfterEachApplierCard()
    {
        var cs = NewCombat(1);
        var m = cs.Monsters[0];
        var strangle = Assert.NotNull(PowerCmd.Apply<StranglePower>(cs, m, 2m, cs.Player, null));
        var card = new CardInstance(new SkillDouble(), 300);
        var play = new CardPlay(cs, card, null);
        strangle.BeforeCardPlayed(cs, play);
        strangle.AfterCardPlayed(cs, play);
        Assert.Equal(48, m.CurrentHp);
        strangle.AfterCardPlayed(cs, play);
        Assert.Equal(48, m.CurrentHp);
    }

    public static void Test_Content_powers_2_2_PanacheFiresOnFifthCardAfterTheFirst()
    {
        var cs = NewCombat(2);
        var panache = Assert.NotNull(PowerCmd.Apply<PanachePower>(cs, cs.Player, 10m, cs.Player, null));
        var card = new CardInstance(new SkillDouble(), 301);
        for (int i = 0; i < 5; i++)
        {
            panache.AfterCardPlayed(cs, new CardPlay(cs, card, null));
        }
        Assert.Equal(50, cs.Monsters[0].CurrentHp);
        panache.AfterCardPlayed(cs, new CardPlay(cs, card, null));
        Assert.Equal(40, cs.Monsters[0].CurrentHp);
        Assert.Equal(40, cs.Monsters[1].CurrentHp);
        Assert.Equal(PanachePower.BaseCardsLeft, panache.CardsLeft);
    }
}
