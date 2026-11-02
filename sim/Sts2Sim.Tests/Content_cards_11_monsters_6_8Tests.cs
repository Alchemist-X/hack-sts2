using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_11_monsters_6_8Tests
{
    private static readonly string[] PortedCardIds =
    {
        "BYRDONIS_EGG", "CREATIVE_AI", "DEPRECATED_CARD", "DOWSING", "GENETIC_ALGORITHM", "GUILTY",
        "LANTERN_KEY", "MISERY", "SPOILS_MAP", "THE_HUNT", "THE_SCYTHE", "WHITE_NOISE"
    };

    private static readonly string[] PortedMonsterIds =
    {
        "BATTLE_FRIEND_V1", "BATTLE_FRIEND_V2", "BATTLE_FRIEND_V3", "FAT_GREMLIN", "GREMLIN_MERC",
        "THIEVING_HOPPER", "BYRDPIP", "PAELS_LEGION"
    };

    public static void Test_Content_cards_11_monsters_6_8_AllPortedIdsRegistered()
    {
        foreach (var id in PortedCardIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.Equal(id, def.Id, $"{id} id");
            Assert.False(def is UnportedCardDef, $"{id} is a ported def");
            def.CreateVars();
        }
        foreach (var id in PortedMonsterIds)
        {
            Assert.True(MonsterRegistry.TryGet(id, out var def), $"monster {id} is registered");
            Assert.Equal(id, def!.Id);
        }
    }

    public static void Test_Content_cards_11_monsters_6_8_TheScytheGrowsCombatCopyAndDeckVersion()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var def = CardRegistry.Get(TheScythe.CardId);
        var deckCard = new CardInstance(def, cs.Run.AllocateCardInstanceId());
        deckCard.AttachTo(player);
        player.Deck.Add(deckCard);
        var card = cs.CreateCard(def, player);
        card.DeckVersionId = deckCard.InstanceId;
        CardPileCmd.Add(cs, card, PileType.Hand);
        var monster = cs.Monsters[0];
        card.OnPlay(cs, new CardPlay(cs, card, monster));
        Assert.Equal(100 - 13, monster.CurrentHp, "first hit 13");
        Assert.Equal(18, TheScythe.GetCurrentDamage(card), "combat copy +5");
        Assert.Equal(18, TheScythe.GetCurrentDamage(deckCard), "deck version +5");
        Assert.Equal(18m, deckCard.Vars.Damage.BaseValue, "deck damage var follows");
    }

    public static void Test_Content_cards_11_monsters_6_8_GuiltyLeavesDeckAfterFiveCombats()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var guilty = new CardInstance(CardRegistry.Get(Guilty.CardId), cs.Run.AllocateCardInstanceId());
        guilty.AttachTo(player);
        player.Deck.Add(guilty);
        for (int i = 0; i < 4; i++)
        {
            Hook.AfterCombatEnd(cs.Run, null, null);
        }
        Assert.True(player.Deck.Contains(guilty), "still in deck after 4 combats");
        Assert.Equal(1m, guilty.Vars[Guilty.CombatsKey].BaseValue, "one combat left");
        Hook.AfterCombatEnd(cs.Run, null, null);
        Assert.False(player.Deck.Contains(guilty), "removed after 5 combats");
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "NECROBINDER", 80, 80, 3) { Energy = 3 };
        var cs = new CombatState(player, "CONTENT_CARDS_11_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), new DummyTarget(), 100, 100, null, null));
        return cs;
    }

    private sealed class DummyTarget : MonsterDef
    {
        public DummyTarget()
            : base("CONTENT_CARDS_11_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 100;

        public override int MaxInitialHp(CombatState cs) => 100;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
