using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_2_4_6Tests
{
    private static readonly string[] PortedIds =
    {
        "AKABEKO", "DIAMOND_DIADEM", "FAKE_SNECKO_EYE", "FUNERARY_MASK", "HELICAL_DART", "SELF_FORMING_CLAY",
        "SNECKO_EYE", "SNECKO_SKULL", "TWISTED_FUNNEL", "UNDYING_SIGIL", "BELT_BUCKLE", "BIG_HAT", "CROSSBOW",
        "DELICATE_FROND", "HISTORY_COURSE", "MYSTIC_LIGHTER", "ORANGE_DOUGH", "PAELS_EYE", "PETRIFIED_TOAD",
        "REPTILE_TRINKET", "RIPPLE_BASIN", "VEXING_PUZZLEBOX", "CHOICES_PARADOX", "GAMBLING_CHIP",
        "TOASTY_MITTENS", "TOOLBOX",
    };

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(12345UL), 0, null);
        var cs = new CombatState(run, player, "RELICS_2_4_6_TEST");
        cs.IsInProgress = true;
        var def = MonsterRegistry.Get(MonsterRegistry.AllIds[0]);
        cs.Monsters.Add(new MonsterState(1, def, 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    public static void Test_ContentRelics246_AllIdsRegistered()
    {
        var all = new HashSet<string>(RelicRegistry.AllIds);
        foreach (var id in PortedIds)
        {
            Assert.True(all.Contains(id), "relic registered: " + id);
            Assert.Equal(id, RelicRegistry.Create(id).Id, "id round-trips: " + id);
        }
    }

    public static void Test_ContentRelics246_BeltBuckleTogglesDexterityWithPotions()
    {
        var cs = NewCombat();
        var relic = Equip(cs, new BeltBuckle());
        relic.BeforeCombatStart(cs.Run);
        Assert.Equal(2, cs.Player.GetPowerAmount<DexterityPower>(), "no potions at combat start: +2 Dexterity");
        Assert.Equal(RelicStatus.Active, relic.Status, "active while no potions");
        var potion = PotionRegistry.Create("STRENGTH_POTION");
        potion.AttachTo(cs.Player);
        cs.Player.PotionSlots[0] = potion;
        relic.AfterPotionProcured(cs.Run, potion);
        Assert.Equal(0, cs.Player.GetPowerAmount<DexterityPower>(), "procuring a potion removes the Dexterity");
        Assert.Equal(RelicStatus.Normal, relic.Status, "normal while a potion is held");
        relic.AfterPotionProcured(cs.Run, potion);
        Assert.Equal(0, cs.Player.GetPowerAmount<DexterityPower>(), "removal happens once");
    }

    public static void Test_ContentRelics246_PaelsEyeExtraTurnOncePerCombat()
    {
        var cs = NewCombat();
        var relic = Equip(cs, new PaelsEye());
        cs.Player.TurnNumber = 2;
        Assert.True(relic.ShouldTakeExtraTurn(cs, cs.Player), "no card played this turn: extra turn");
        relic.AfterTakingExtraTurn(cs, cs.Player);
        Assert.False(relic.ShouldTakeExtraTurn(cs, cs.Player), "used once this combat");
        relic.AfterCombatEnd(cs.Run, null);
        Assert.True(relic.ShouldTakeExtraTurn(cs, cs.Player), "reset at combat end");
        var snecko = Equip(cs, new SneckoEye());
        Assert.Equal(7m, snecko.ModifyHandDraw(cs, cs.Player, 5m), "Snecko Eye +2 hand draw");
    }
}
