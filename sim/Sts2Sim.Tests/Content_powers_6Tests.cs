using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_powers_6Tests
{
    private static readonly string[] PortedIds =
    {
        "BIASED_COGNITION_POWER", "BLACK_HOLE_POWER", "CALCIFY_POWER", "CHILD_OF_THE_STARS_POWER",
        "CONQUEROR_POWER", "CONSUMING_SHADOW_POWER", "COOLANT_POWER", "DEVOUR_LIFE_POWER",
        "FOCUSED_STRIKE_POWER", "FURNACE_POWER", "GENESIS_POWER", "HAILSTORM_POWER",
        "HIBERNATE_POWER", "HOTFIX_POWER", "HYPERBEAM_FOCUS_DOWN_POWER", "LIGHTNING_ROD_POWER",
        "LOOP_POWER", "NECRO_MASTERY_POWER", "PARRY_POWER", "SEEKING_EDGE_POWER",
        "SIC_EM_POWER", "SPINNER_POWER", "STAR_NEXT_TURN_POWER", "STORM_POWER",
        "SUMMON_NEXT_TURN_POWER", "SWORD_SAGE_POWER", "SYNCHRONIZE_POWER", "THE_SEALED_THRONE_POWER",
        "THUNDER_POWER", "TRASH_TO_TREASURE_POWER", "VOID_FORM_POWER",
        "DIE_FOR_YOU_POWER", "FOCUS_POWER",
    };

    private sealed class Powers6MonsterDouble : MonsterDef
    {
        public Powers6MonsterDouble()
            : base("POWERS_6_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat(int monsters = 1, int monsterHp = 50)
    {
        var player = new PlayerState(0, "DEFECT", 80, 80, 3) { BaseOrbSlotCount = 3, Energy = 3 };
        var cs = new CombatState(player, "POWERS_6_TEST", 0, new RunRngSet(new Rng(777UL)));
        cs.IsInProgress = true;
        player.OrbQueue.ResetForNewCombat(3);
        var def = new Powers6MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, monsterHp, monsterHp, null, null));
        }
        return cs;
    }

    private static void Enqueue<T>(PlayerState owner) where T : OrbModel, new()
    {
        var orb = new T();
        orb.AttachTo(owner);
        Assert.True(owner.OrbQueue.TryEnqueue(orb), "test setup enqueues into a free slot");
    }

    public static void Test_Content_powers_6_AllIdsRegistered()
    {
        var ids = new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in PortedIds)
        {
            Assert.True(ids.Contains(id), $"{id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id, $"{id} round-trips through the registry");
        }
    }

    public static void Test_Content_powers_6_CoolantBlockPerDistinctOrbType()
    {
        var cs = NewCombat();
        var player = cs.Player;
        Enqueue<FrostOrb>(player);
        Enqueue<FrostOrb>(player);
        Enqueue<LightningOrb>(player);
        var coolant = new CoolantPower();
        coolant.ApplyInternal(player, 3);
        coolant.AfterSideTurnStart(cs, CombatSide.Player, new CreatureState[] { player });
        Assert.Equal(6, player.Block, "two distinct orb ids x 3");
    }

    public static void Test_Content_powers_6_VoidFormCounterHiddenUntilTurnStart()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var voidForm = new VoidFormPower();
        voidForm.BeforeApplied(cs, player, 2, player, null);
        voidForm.ApplyInternal(player, 2);
        Assert.Equal(VoidFormPower.HiddenCardsPlayedThisTurn, voidForm.CardsPlayedThisTurn, "applying hides the zero-cost window");
        voidForm.BeforeSideTurnStart(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[0] });
        Assert.Equal(VoidFormPower.HiddenCardsPlayedThisTurn, voidForm.CardsPlayedThisTurn, "another side's turn start does not reset");
        voidForm.BeforeSideTurnStart(cs, CombatSide.Player, new CreatureState[] { player });
        Assert.Equal(0, voidForm.CardsPlayedThisTurn, "the owner's side turn start resets the counter");
    }
}
