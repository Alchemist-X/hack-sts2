using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_7_2Tests
{
    private static readonly string[] PortedIds =
    {
        "MAW_BANK", "MEAL_TICKET", "NEOWS_SACRIFICE", "NEOWS_TALISMAN", "NEOWS_TORMENT", "NUTRITIOUS_OYSTER",
        "NUTRITIOUS_SOUP", "OLD_COIN", "PAELS_CLAW", "PAELS_HORN", "PANDORAS_BOX", "PEAR", "PHIAL_HOLSTER",
        "PLANISPHERE", "POTION_BELT", "SAND_CASTLE", "SERE_TALON", "SIGNET_RING", "STORYBOOK", "STRAWBERRY",
        "TANXS_WHISTLE", "TOUCH_OF_OROBAS", "VENERABLE_TEA_SET", "WAR_HAMMER", "WAR_PAINT"
    };

    private static RunState NewRun()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(7272UL), 0, null);
        foreach (var id in StartingDecks.Ironclad)
        {
            player.Deck.Add(CardFactory.CreateRunCard(rs, CardRegistry.Get(id), player));
        }
        return rs;
    }

    private static T Obtain<T>(RunState rs, T relic) where T : RelicModel
    {
        RelicCmd.Obtain(rs, relic, rs.Players[0]);
        return relic;
    }

    public static void Test_Content_relics_7_2_AllPortedIdsAreRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(RelicRegistry.Models.Contains(id), $"relic {id} is not registered");
            Assert.Equal(id, RelicRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_relics_7_2_SereTalonLosesMaxHpAndAddsWishes()
    {
        var rs = NewRun();
        var player = rs.Players[0];
        Obtain(rs, new SereTalon());
        Assert.Equal(71, player.MaxHp);
        Assert.Equal(71, player.CurrentHp);
        Assert.Equal(13, player.Deck.Count);
        Assert.Equal(3, player.Deck.Count(c => c.Id == SereTalon.WishCardId));
    }

    public static void Test_Content_relics_7_2_TouchOfOrobasRefinesStarter()
    {
        var rs = NewRun();
        var player = rs.Players[0];
        RelicCmd.Obtain(rs, "BURNING_BLOOD", player);
        var touch = new TouchOfOrobas();
        Assert.True(touch.SetupForPlayer(rs, player));
        Assert.Equal("BLACK_BLOOD", touch.UpgradedRelic);
        Obtain(rs, touch);
        Assert.Equal("BLACK_BLOOD", player.Relics[0].Id);
        Assert.True(player.Relics.All(r => r.Id != "BURNING_BLOOD"));
    }

    public static void Test_Content_relics_7_2_PandorasBoxTransformsBasics()
    {
        W1Pending.Run(nameof(Test_Content_relics_7_2_PandorasBoxTransformsBasics), "cards (ironclad pool)", () =>
        {
            var rs = NewRun();
            var player = rs.Players[0];
            Obtain(rs, new PandorasBox());
            Assert.Equal(10, player.Deck.Count);
            Assert.Equal(0, player.Deck.Count(c => c.IsBasicStrikeOrDefend));
            Assert.Equal(1, player.Deck.Count(c => c.Id == "BASH"));
        });
    }
}
