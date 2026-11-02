using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_8_1Tests
{
    private static readonly string[] PortedIds =
    {
        "AMETHYST_AUBERGINE", "ARCANE_SCROLL", "BLACK_STAR", "BOWLER_HAT", "CALLING_BELL", "CAULDRON", "DINGY_RUG",
        "DREAM_CATCHER", "DRIFTWOOD", "ECTOPLASM", "FRESNEL_LENS", "FROZEN_EGG", "GIRYA", "GLASS_EYE", "GLITTER",
        "JUZU_BRACELET", "KALEIDOSCOPE", "LARGE_CAPSULE", "LASTING_CANDY", "LAVA_LAMP", "LAVA_ROCK", "LOST_COFFER",
        "MEAT_CLEAVER", "MEMBERSHIP_CARD", "MINIATURE_TENT",
    };

    private static (RunState Rs, PlayerState Player) NewRun(ulong seed)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), 0);
        player.PlayerRng = new PlayerRngSet(seed + 1);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, 0);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(rs, player, rs.Rng.UpFront);
        rs.SharedRelicGrabBag = new RelicGrabBag(true);
        rs.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(rs), rs.Rng.UpFront);
        rs.Odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
        return (rs, player);
    }

    private static T Equip<T>(PlayerState player, T relic) where T : RelicModel
    {
        relic.AttachTo(player);
        player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_8_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
        }
    }

    public static void Test_Content_relics_8_1_GoldAndEnergyModifiers()
    {
        var (rs, player) = NewRun(31UL);
        var hat = Equip(player, new BowlerHat());
        Assert.Equal(25m, hat.ModifyGoldGained(rs, player, 20m), "bowler hat x1.25");
        var ecto = Equip(player, new Ectoplasm());
        Assert.Equal(0m, ecto.ModifyGoldGained(rs, player, 20m), "ectoplasm blocks gold");
        Assert.Equal(4m, ecto.ModifyMaxEnergy(null!, player, 3m), "ectoplasm +1 energy");
        var juzu = Equip(player, new JuzuBracelet());
        var types = juzu.ModifyUnknownMapPointRoomTypes(rs, new HashSet<RoomType> { RoomType.Monster, RoomType.Event, RoomType.Shop });
        Assert.True(!types.Contains(RoomType.Monster) && types.Count == 2, "juzu removes monster");
    }

    public static void Test_Content_relics_8_1_GiryaLiftAndArcaneScroll()
    {
        var (rs, player) = NewRun(47UL);
        var girya = Equip(player, new Girya());
        var options = new List<RestSiteOption>();
        Assert.True(girya.TryModifyRestSiteOptions(rs, player, options), "lift offered");
        Assert.True(options[0].OnSelect(rs), "lift selected");
        Assert.Equal(1, girya.TimesLifted, "lifted once");
        girya.TimesLifted = Girya.MaxLifts;
        Assert.True(!girya.TryModifyRestSiteOptions(rs, player, new List<RestSiteOption>()), "no lift after max");

        int before = player.Deck.Count;
        var scroll = Equip(player, new ArcaneScroll());
        scroll.AfterObtained(rs);
        Assert.Equal(before + 1, player.Deck.Count, "one card added");
        Assert.Equal(CardRarity.Rare, player.Deck[player.Deck.Count - 1].Rarity, "rare card");
    }
}
