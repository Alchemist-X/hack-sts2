using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_7_1Tests
{
    private static readonly string[] PortedIds =
    {
        "BALL_LIGHTNING", "BARRAGE", "CHILL", "COLD_SNAP", "COMPILE_DRIVER", "COOLHEADED", "DEFRAGMENT", "DUALCAST",
        "FOCUSED_STRIKE", "FUSION", "GLACIER", "GLASSWORK", "HOTFIX", "HYPERBEAM", "ICE_LANCE", "METEOR_STRIKE",
        "NULL", "RAINBOW", "REFRACT", "SHADOW_SHIELD", "SYNCHRONIZE", "TEMPEST", "ZAP"
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("CONTENT_CARDS_7_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    public static void Test_Content_cards_7_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.False(def is UnportedCardDef, id + " is a real port");
            Assert.Equal(id, def.Id);
        }
    }

    public static void Test_Content_cards_7_1_GlacierBlocksThenChannelsTwoFrost()
    {
        var cs = NewCombat(baseOrbSlots: 3);
        var glacier = AddCard(cs, CardRegistry.Get("GLACIER"));
        glacier.Def.OnPlay(cs, new CardPlay(cs, glacier, null));
        Assert.Equal(6, cs.Player.Block);
        Assert.Equal(2, cs.Player.OrbQueue.Orbs.Count);
        Assert.True(cs.Player.OrbQueue.Orbs.All(o => o is FrostOrb));
    }

    public static void Test_Content_cards_7_1_BarrageHitsOncePerOrb()
    {
        var cs = NewCombat(baseOrbSlots: 3);
        var player = cs.Player;
        for (int i = 0; i < 3; i++)
        {
            var orb = new FrostOrb();
            orb.AttachTo(player);
            Assert.True(player.OrbQueue.TryEnqueue(orb));
        }
        var barrage = AddCard(cs, CardRegistry.Get("BARRAGE"));
        var monster = cs.Monsters[0];
        barrage.Def.OnPlay(cs, new CardPlay(cs, barrage, monster));
        Assert.Equal(50 - 3 * 5, monster.CurrentHp);
    }

    private static CombatState NewCombat(int baseOrbSlots)
    {
        var player = new PlayerState(0, "DEFECT", 80, 80, 3) { BaseOrbSlotCount = baseOrbSlots, Energy = 3 };
        var cs = new CombatState(player, "CONTENT_CARDS_7_1_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        player.OrbQueue.AddCapacity(baseOrbSlots);
        var def = new MonsterDouble();
        cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, 50, 50, null, null));
        return cs;
    }

    private static CardInstance AddCard(CombatState cs, CardDef def)
    {
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        cs.Player.Hand.AddInternal(card);
        return card;
    }
}
