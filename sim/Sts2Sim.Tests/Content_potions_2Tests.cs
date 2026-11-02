using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_potions_2Tests
{
    private static readonly string[] PortedIds =
    {
        "REGEN_POTION", "SHACKLING_POTION", "SHIP_IN_A_BOTTLE", "SPEED_POTION", "STABLE_SERUM",
        "BLESSING_OF_THE_FORGE", "BOTTLED_POTENTIAL", "CUNNING_POTION", "POT_OF_GHOULS", "SNECKO_OIL",
        "SOLDIERS_STEW", "BONE_BREW", "COSMIC_CONCOCTION", "DISTILLED_CHAOS", "ENTROPIC_BREW",
        "ESSENCE_OF_DARKNESS", "KINGS_COURAGE", "OROBIC_ACID", "POTION_OF_CAPACITY", "POTION_OF_DOOM",
        "STAR_POTION", "COLORLESS_POTION", "DROPLET_OF_PRECOGNITION", "GAMBLERS_BREW", "LIQUID_MEMORIES",
        "POWER_POTION", "SKILL_POTION", "TOUCH_OF_INSANITY", "FOUL_POTION",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("CONTENT_POTIONS_2_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(12345UL), 0);
        var cs = new CombatState(run, player, "CONTENT_POTIONS_2_TEST");
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static CardInstance AddCard(CombatState cs, CardPile pile, string cardId)
    {
        var card = new CardInstance(CardRegistry.Get(cardId), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        pile.AddInternal(card);
        return card;
    }

    private static T Slot<T>(CombatState cs, T potion) where T : PotionModel
    {
        potion.AttachTo(cs.Player);
        cs.Player.PotionSlots[0] = potion;
        return potion;
    }

    public static void Test_ContentPotions2_EveryPortedIdIsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(PotionRegistry.Models.Contains(id), id + " is registered");
            Assert.Equal(id, PotionRegistry.Create(id).Id);
        }
    }

    public static void Test_ContentPotions2_SoldiersStewAddsAReplayToEveryStrikeInCombat()
    {
        var cs = NewCombat();
        var handStrike = AddCard(cs, cs.Player.Hand, StrikeIronclad.CardId);
        var drawStrike = AddCard(cs, cs.Player.DrawPile, StrikeIronclad.CardId);
        var defend = AddCard(cs, cs.Player.DiscardPile, DefendIronclad.CardId);
        Slot(cs, new SoldiersStew()).OnUseWrapper(cs.Run, cs.Player);
        Assert.Equal(1, handStrike.BaseReplayCount);
        Assert.Equal(1, drawStrike.BaseReplayCount);
        Assert.Equal(0, defend.BaseReplayCount);
        Assert.Null(cs.Player.PotionSlots[0]);
    }

    public static void Test_ContentPotions2_BottledPotentialShufflesTheHandInAndDrawsFive()
    {
        var cs = NewCombat();
        for (int i = 0; i < 3; i++)
        {
            AddCard(cs, cs.Player.Hand, StrikeIronclad.CardId);
        }
        for (int i = 0; i < 4; i++)
        {
            AddCard(cs, cs.Player.DrawPile, DefendIronclad.CardId);
        }
        Slot(cs, new BottledPotential()).OnUseWrapper(cs.Run, cs.Player);
        Assert.Equal(5, cs.Player.Hand.Cards.Count);
        Assert.Equal(2, cs.Player.DrawPile.Cards.Count);
    }
}
