using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_7_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ALCHEMICAL_COFFER", "ARCHAIC_TOOTH", "BEAUTIFUL_BRACELET", "BIG_MUSHROOM", "BING_BONG", "BLOOD_SOAKED_ROSE",
        "BOOK_OF_FIVE_RINGS", "CURSED_PEARL", "DARKSTONE_PERIAPT", "DISTINGUISHED_CAPE", "DOWSING_ROD", "DRAGON_FRUIT",
        "DUSTY_TOME", "ETERNAL_FEATHER", "FAKE_LEES_WAFFLE", "FAKE_MANGO", "FAKE_VENERABLE_TEA_SET", "FISHING_ROD",
        "FRAGRANT_MUSHROOM", "GOLDEN_PEARL", "JEWELRY_BOX", "LEAFY_POULTICE", "LEES_WAFFLE", "LOOMING_FRUIT",
        "LUCKY_FYSH", "MANGO",
    };

    private static (RunState Rs, PlayerState Player) NewRun(string character)
    {
        var player = new PlayerState(0, character, 60, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0, null);
        return (rs, player);
    }

    private static CardInstance AddToDeck(RunState rs, PlayerState player, string cardId)
    {
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(cardId), player);
        player.Deck.Add(card);
        return card;
    }

    private static T Equip<T>(PlayerState player, T relic) where T : RelicModel
    {
        relic.AttachTo(player);
        player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_7_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
        }
    }

    public static void Test_Content_relics_7_1_BingBongDuplicatesDeckAddsOnce()
    {
        var (rs, player) = NewRun("IRONCLAD");
        Equip(player, new BingBong());
        var book = Equip(player, new BookOfFiveRings());
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get("BASH"), player);
        CardPileCmd.Add(rs, card, PileType.Deck);
        Assert.Equal(2, player.Deck.Count, "original plus one clone");
        Assert.Equal(2, book.CardsAdded, "book counts the clone too");
    }

    public static void Test_Content_relics_7_1_ArchaicToothTransformsStarterOutOfCombat()
    {
        var (rs, player) = NewRun("IRONCLAD");
        AddToDeck(rs, player, "STRIKE_IRONCLAD");
        var bash = AddToDeck(rs, player, "BASH");
        CardCmd.Upgrade(null, bash);
        var tooth = new ArchaicTooth();
        Assert.True(tooth.SetupForPlayer(rs, player), "setup finds Bash");
        Assert.Equal("BREAK", tooth.AncientCard, "ancient id");
        Equip(player, tooth);
        tooth.AfterObtained(rs);
        Assert.Equal(2, player.Deck.Count, "deck size kept");
        var ancient = player.Deck[1];
        Assert.Equal("BREAK", ancient.Id, "Bash became Break at the end of the deck");
        Assert.True(ancient.IsUpgraded, "upgrade carried over");
        Assert.True(bash.HasBeenRemovedFromState, "original removed");
    }
}
