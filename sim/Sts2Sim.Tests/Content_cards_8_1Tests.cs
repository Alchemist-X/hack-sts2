using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_8_1Tests
{
    private static readonly string[] PortedIds =
    {
        "BULK_UP", "CAPACITOR", "CHAOS", "CONSUMING_SHADOW", "COOLANT", "DARKNESS", "HAILSTORM", "LIGHTNING_ROD",
        "LOOP", "MODDED", "MULTI_CAST", "QUADCAST", "SHATTER", "SPINNER", "STORM", "TESLA_COIL", "THUNDER",
        "TRASH_TO_TREASURE", "VOLTAIC"
    };

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CARDS_8_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat(int baseOrbSlots = 3)
    {
        var player = new PlayerState(0, "DEFECT", 80, 80, 3) { BaseOrbSlotCount = baseOrbSlots, Energy = 3 };
        var cs = new CombatState(player, "CARDS_8_1_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        player.OrbQueue.AddCapacity(baseOrbSlots);
        cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), new DummyMonster(), 50, 50, null, null));
        return cs;
    }

    private static CardInstance InHand(CombatState cs, string id)
    {
        var card = cs.CreateCard(CardRegistry.Get(id), cs.Player);
        cs.Player.GetPile(PileType.Hand).AddInternal(card);
        return card;
    }

    public static void Test_Cards_8_1_AllPortedIdsAreRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.Equal(id, def.Id);
        }
    }

    public static void Test_Cards_8_1_ShatterEvokesEachOrbTwiceThenDequeues()
    {
        var cs = NewCombat();
        OrbCmd.Channel<FrostOrb>(cs, cs.Player);
        OrbCmd.Channel<FrostOrb>(cs, cs.Player);
        var shatter = InHand(cs, "SHATTER");
        shatter.Def.OnPlay(cs, new CardPlay(cs, shatter, null));
        Assert.Equal(0, cs.Player.OrbQueue.Orbs.Count, "every orb is dequeued");
        Assert.Equal(20, cs.Player.Block, "two frost orbs evoked twice each give 4 x 5 block");
        Assert.Equal(43, cs.Monsters[0].CurrentHp, "7 damage to all enemies");
    }

    public static void Test_Cards_8_1_VoltaicChannelsOneLightningPerLightningChanneledThisCombat()
    {
        var cs = NewCombat(baseOrbSlots: 10);
        OrbCmd.Channel<LightningOrb>(cs, cs.Player);
        OrbCmd.Channel<FrostOrb>(cs, cs.Player);
        OrbCmd.Channel<LightningOrb>(cs, cs.Player);
        var voltaic = InHand(cs, "VOLTAIC");
        voltaic.Def.OnPlay(cs, new CardPlay(cs, voltaic, null));
        Assert.Equal(5, cs.Player.OrbQueue.Orbs.Count, "two lightning channeled before means two more");
        Assert.Equal(4, cs.Player.OrbQueue.Orbs.Count(o => o is LightningOrb));
    }
}
