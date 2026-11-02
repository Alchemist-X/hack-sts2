using Sts2Sim.Core;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_8_2Tests
{
    private static readonly string[] PortedIds =
    {
        "MOLTEN_EGG", "NEOWS_BONES", "ORRERY", "PAELS_WING", "PRAYER_WHEEL", "PRISMATIC_GEM", "PUMPKIN_CANDLE",
        "REGAL_PILLOW", "SHOVEL", "SILKEN_TRESS", "SILVER_CRUCIBLE", "SMALL_CAPSULE", "SOZU", "STONE_HUMIDIFIER",
        "SWORD_OF_STONE", "THE_COURIER", "TINY_MAILBOX", "TOXIC_EGG", "TOY_BOX", "WHITE_BEAST_STATUE", "WHITE_STAR",
        "WINGED_BOOTS", "WING_CHARM", "WONGOS_MYSTERY_TICKET",
    };

    private static RunState NewRun()
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        return RunState.CreateStandalone(player, new RunRngSet(882UL), 0, null);
    }

    private static T Equip<T>(RunState rs, T relic) where T : RelicModel
    {
        var player = rs.Players[0];
        relic.AttachTo(player);
        player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_8_2_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
        }
    }

    public static void Test_Content_relics_8_2_PumpkinCandleKindles()
    {
        var rs = NewRun();
        var candle = Equip(rs, new PumpkinCandle());
        candle.AfterObtained(rs);
        Assert.Equal(5, candle.KindleCount, "obtained with 5");
        Assert.Equal(4m, candle.ModifyMaxEnergy(null!, rs.Players[0], 3m), "+1 energy while lit");
        for (int i = 0; i < 6; i++)
        {
            candle.AfterCombatEnd(rs, null);
        }
        Assert.Equal(0, candle.KindleCount, "floors at 0");
        Assert.Equal(3m, candle.ModifyMaxEnergy(null!, rs.Players[0], 3m), "no energy when out");
        Assert.True(candle.Status == RelicStatus.Disabled, "disabled when out");
        Assert.True(new KindleRestSiteOption(rs.Players[0]).OnSelect(rs), "kindle option completes");
        Assert.Equal(5, candle.KindleCount, "rekindled");
    }

    public static void Test_Content_relics_8_2_WongosTicketGatesOnFiveCombats()
    {
        var rs = NewRun();
        var ticket = Equip(rs, new WongosMysteryTicket());
        var room = new CombatRoom(EncounterRegistry.Get(EncounterRegistry.AllIds[0]));
        var rewards = new List<Reward>();
        for (int i = 0; i < 4; i++)
        {
            ticket.AfterCombatEnd(rs, null);
        }
        Assert.True(!ticket.TryModifyRewards(rs, rs.Players[0], rewards, room), "inactive before 5 combats");
        ticket.AfterCombatEnd(rs, null);
        Assert.True(ticket.TryModifyRewards(rs, rs.Players[0], rewards, room), "active after 5 combats");
        Assert.Equal(3, rewards.Count, "three relic rewards");
        ticket.AfterModifyingRewards(rs);
        Assert.True(ticket.GaveRelic && ticket.IsUsedUp, "used up");
    }
}
