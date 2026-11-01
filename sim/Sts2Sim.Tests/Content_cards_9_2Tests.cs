using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_9_2Tests
{
    private static readonly string[] PortedIds =
    {
        "NEUTRON_AEGIS", "PARRY", "PARTICLE_WALL", "POKE", "PROTECTOR", "PULL_AGGRO", "QUASAR", "RADIATE",
        "RATTLE", "REANIMATE", "REFINE_BLADE", "REFLECT", "RESONANCE", "RIGHT_HAND_HAND", "ROYAL_GAMBLE",
        "SACRIFICE", "SEEKING_EDGE", "SENTRY_MODE", "SEVEN_STARS", "SHINING_STRIKE", "SIC_EM", "SNAP",
        "SOLAR_STRIKE", "SPOILS_OF_BATTLE", "SPUR", "SQUEEZE", "STARDUST", "SUMMON_FORTH", "SWEEPING_GAZE",
        "SWORD_SAGE", "THE_SEALED_THRONE", "THE_SMITH", "UNLEASH", "VENERATE", "WROUGHT_IN_WAR"
    };

    public static void Test_Content_cards_9_2_AllPortedIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.Equal(id, def.Id, $"{id} id");
            Assert.False(def is UnportedCardDef, $"{id} is a ported def");
            def.CreateVars();
        }
    }

    public static void Test_Content_cards_9_2_SacrificeKillsOstyAndGainsThreeTimesItsMaxHp()
    {
        var cs = NewCombat();
        var player = cs.Player;
        OstyCmd.Summon(cs, player, 5m, null);
        Assert.True(CombatStatus.IsOstyAlive(cs, player), "osty summoned");
        var card = cs.CreateCard(CardRegistry.Get(Sacrifice.CardId), player);
        CardPileCmd.Add(cs, card, PileType.Hand);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.False(CombatStatus.IsOstyAlive(cs, player), "osty killed");
        Assert.Equal(15, player.Block, "block = 3 x osty max hp");
    }

    public static void Test_Content_cards_9_2_RadiateHitsOncePerStarGainedThisTurn()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var monster = cs.Monsters[0];
        PlayerCmd.GainStars(cs, 2m, player);
        var card = cs.CreateCard(CardRegistry.Get(Radiate.CardId), player);
        CardPileCmd.Add(cs, card, PileType.Hand);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(50 - 2 * 3, monster.CurrentHp, "two hits of 3");
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "NECROBINDER", 80, 80, 3) { Energy = 3 };
        var cs = new CombatState(player, "CONTENT_CARDS_9_2_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), new DummyTarget(), 50, 50, null, null));
        return cs;
    }

    private sealed class DummyTarget : MonsterDef
    {
        public DummyTarget()
            : base("CONTENT_CARDS_9_2_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
