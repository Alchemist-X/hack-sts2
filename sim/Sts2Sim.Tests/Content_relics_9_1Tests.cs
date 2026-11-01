using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_9_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ASTROLABE", "BIIIG_HUG", "CLAWS", "DOLLYS_MIRROR", "ELECTRIC_SHRYMP", "EMPTY_CAGE", "GNARLED_HAMMER", "HEFTY_TABLET",
        "KIFUDA", "LEAD_PAPERWEIGHT", "NEW_LEAF", "PAELS_GROWTH", "PAELS_TOOTH", "POMANDER", "PRECARIOUS_SHEARS", "PRECISE_SCISSORS",
        "PRESERVED_FOG", "PUNCH_DAGGER", "ROYAL_STAMP", "SCROLL_BOXES", "SEA_GLASS", "TRI_BOOMERANG",
        "YUMMY_COOKIE",
    };

    internal sealed class PickFirst : IChoiceAnswerer
    {
        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            int k = prompt.MinSelect > 0 ? prompt.MinSelect : Math.Min(1, prompt.MaxSelect);
            return new ChoiceAnswer(Enumerable.Range(0, Math.Min(k, prompt.Options.Count)).ToArray(), false);
        }
    }

    internal static void WithObtainPrompts(RunState rs, PlayerState player, string seedText, Action body)
    {
        if (player.PlayerRng == null)
        {
            rs.InitializeSeed(player, seedText);
        }
        rs.Config ??= new RunConfig(seedText, player.CharacterId, 0) { Content = ContentFilter.PortedOnly };
        if (player.RelicGrabBag == null)
        {
            player.RelicGrabBag = new RelicGrabBag(false);
            player.RelicGrabBag.Populate(rs, player, rs.Rng.UpFront);
        }
        if (rs.SharedRelicGrabBag == null)
        {
            rs.SharedRelicGrabBag = new RelicGrabBag(true);
            rs.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(rs), rs.Rng.UpFront);
        }
        var previous = rs.Choice.Answerer;
        rs.Choice.Answerer = new PickFirst();
        try
        {
            body();
        }
        finally
        {
            rs.Choice.Answerer = previous;
        }
    }

    private static RunState NewRun(params string[] deckIds)
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0, null);
        rs.InitializeSeed(player, "4242");
        foreach (var id in deckIds)
        {
            CardPileCmd.Add(rs, CardFactory.CreateRunCard(rs, CardRegistry.Get(id), player), PileType.Deck);
        }
        rs.Choice.Answerer = new PickFirst();
        return rs;
    }

    public static void Test_Content_relics_9_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
        }
    }

    public static void Test_Content_relics_9_1_PaelsToothRemovesFiveAndReturnsOneUpgraded()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "BASH");
        var player = rs.Players[0];
        var tooth = (PaelsTooth)RelicCmd.Obtain(rs, RelicRegistry.Create("PAELS_TOOTH"), player);
        Assert.Equal(1, player.Deck.Count, "five cards removed");
        Assert.Equal(5, tooth.SerializableCards.Count, "five cards stored");
        tooth.AfterCombatEnd(rs, null);
        Assert.Equal(2, player.Deck.Count, "one card returned");
        Assert.Equal(4, tooth.SerializableCards.Count, "four left");
        Assert.True(player.Deck[^1].IsUpgraded, "returned card is upgraded");
    }

    public static void Test_Content_relics_9_1_PreservedFogRemovesThreeAddsFolly()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        W1Pending.Run("Test_Content_relics_9_1_PreservedFogRemovesThreeAddsFolly", "cards: FOLLY", () =>
        {
            RelicCmd.Obtain(rs, RelicRegistry.Create("PRESERVED_FOG"), player);
            Assert.Equal(2, player.Deck.Count, "three removed, one curse added");
            Assert.Equal("FOLLY", player.Deck[^1].Id, "folly added");
        });
    }

    public static void Test_Content_relics_9_1_AstrolabeTransformsThreeDeckCardsUpgradedOutOfCombat()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        var before = player.Deck.ToList();
        RelicCmd.Obtain(rs, RelicRegistry.Create("ASTROLABE"), player);
        Assert.Equal(4, player.Deck.Count, "transform keeps the deck size");
        int transformed = player.Deck.Count(c => !before.Contains(c));
        Assert.Equal(3, transformed, "three cards replaced");
        foreach (var card in player.Deck.Where(c => !before.Contains(c)))
        {
            Assert.True(card.IsUpgraded || !card.IsUpgradable, card.Id + " upgraded");
            Assert.Equal(rs.TotalFloor, card.FloorAddedToDeck ?? -1, card.Id + " floor added to deck");
        }
    }

    public static void Test_Content_relics_9_1_NewLeafTransformsOneDeckCardOutOfCombat()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        var before = player.Deck.ToList();
        RelicCmd.Obtain(rs, RelicRegistry.Create("NEW_LEAF"), player);
        Assert.Equal(2, player.Deck.Count);
        Assert.Equal(1, player.Deck.Count(c => !before.Contains(c)), "one card replaced");
    }

    public static void Test_Content_relics_9_1_ClawsTransformsIntoMaulOutOfCombat()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        RelicCmd.Obtain(rs, RelicRegistry.Create("CLAWS"), player);
        Assert.Equal(2, player.Deck.Count);
        Assert.Equal("MAUL", player.Deck[^1].Id, "the picked card became a maul");
    }

    public static void Test_Content_relics_9_1_PrecariousShearsRemovesTwoAndDamagesOutOfCombat()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        RelicCmd.Obtain(rs, RelicRegistry.Create("PRECARIOUS_SHEARS"), player);
        Assert.Equal(1, player.Deck.Count, "two cards removed");
        Assert.Equal(80 - 16, player.CurrentHp, "16 unpowered damage");
    }

    public static void Test_Content_relics_9_1_PrecariousShearsKillsOutOfCombatUnlessLizardTail()
    {
        var rs = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player = rs.Players[0];
        player.CurrentHp = 10;
        RelicCmd.Obtain(rs, RelicRegistry.Create("LIZARD_TAIL"), player);
        RelicCmd.Obtain(rs, RelicRegistry.Create("PRECARIOUS_SHEARS"), player);
        Assert.False(player.IsDead, "lizard tail prevents the death");
        Assert.Equal(40, player.CurrentHp, "healed to half of 80");
        var rs2 = NewRun("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD");
        var player2 = rs2.Players[0];
        player2.CurrentHp = 10;
        RelicCmd.Obtain(rs2, RelicRegistry.Create("PRECARIOUS_SHEARS"), player2);
        Assert.True(player2.IsDead, "the run-scope damage kills the player");
    }
}
