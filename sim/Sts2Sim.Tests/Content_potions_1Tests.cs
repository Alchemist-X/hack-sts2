using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_potions_1Tests
{
    private static readonly string[] PortedIds =
    {
        "BEETLE_JUICE", "BLOOD_POTION", "CURE_ALL", "DEXTERITY_POTION", "ENERGY_POTION", "EXPLOSIVE_AMPOULE",
        "FORTIFIER", "FRUIT_JUICE", "FYSH_OIL", "GLOWWATER_POTION", "LIQUID_BRONZE", "POTION_OF_BINDING",
        "POTION_SHAPED_ROCK", "SWIFT_POTION", "VULNERABLE_POTION", "WEAK_POTION", "AMBERGRIS", "CLARITY",
        "DUPLICATOR", "FLEX_POTION", "FOCUS_POTION", "GHOST_IN_A_JAR", "GIGANTIFICATION_POTION", "HEART_OF_IRON",
        "LUCKY_TONIC", "MAZALETHS_GIFT", "POISON_POTION", "POWDERED_DEMISE", "RADIANT_TINCTURE"
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("POTIONS_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat(int monsters)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(12345UL), 0, null);
        var cs = new CombatState(run, player, "POTIONS_1_TEST");
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(i + 1, def, 50, 50, null, null));
        }
        cs.NextCreatureId = monsters + 1;
        return cs;
    }

    private static PotionModel Slot(CombatState cs, string id)
    {
        var potion = PotionRegistry.Create(id);
        potion.AttachTo(cs.Player);
        cs.Player.PotionSlots[0] = potion;
        return potion;
    }

    public static void Test_Content_potions_1_AllPortedIdsRegistered()
    {
        for (int i = 0; i < PortedIds.Length; i++)
        {
            Assert.Equal(PortedIds[i], PotionRegistry.Create(PortedIds[i]).Id);
        }
    }

    public static void Test_Content_potions_1_PotionOfBindingAppliesWeakThenVulnerableToEveryEnemy()
    {
        W1Pending.Run(nameof(Test_Content_potions_1_PotionOfBindingAppliesWeakThenVulnerableToEveryEnemy), "kernel hooks", () =>
        {
            var cs = NewCombat(2);
            Slot(cs, "POTION_OF_BINDING").OnUseWrapper(cs.Run, null);
            for (int i = 0; i < cs.Monsters.Count; i++)
            {
                Assert.Equal(1, cs.Monsters[i].GetPowerAmount<WeakPower>());
                Assert.Equal(1, cs.Monsters[i].GetPowerAmount<VulnerablePower>());
            }
        });
    }

    public static void Test_Content_potions_1_FortifierDoublesCurrentBlock()
    {
        W1Pending.Run(nameof(Test_Content_potions_1_FortifierDoublesCurrentBlock), "kernel hooks", () =>
        {
            var cs = NewCombat(1);
            cs.Player.Block = 7;
            Slot(cs, "FORTIFIER").OnUseWrapper(cs.Run, cs.Player);
            Assert.Equal(21, cs.Player.Block);
        });
    }
}
