using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Registry;

namespace Sts2Sim.Tests;

public static class Content_cards_2_3Tests
{
    private static readonly string[] PortedIds =
    {
        "PREDATOR", "PREP_TIME", "PROLONG", "PYRE", "RAGE", "REAPER_FORM", "RELAX", "ROLLING_BOULDER", "RUPTURE",
        "SALVO", "SCOURGE", "SERPENT_FORM", "SETUP_STRIKE", "SHADOWMELD", "SHROUD", "SIDESTEP", "SIGNAL_BOOST",
        "SLEIGHT_OF_FLESH", "SMOKESTACK", "SNAKEBITE", "SPEEDSTER", "SPIRIT_OF_ASH", "STONE_ARMOR", "STRANGLE",
        "SUBROUTINE", "SYNTHESIS", "TERRAFORMING", "THE_BOMB", "THE_GAMBIT", "TORIC_TOUGHNESS", "TRACKING",
        "UNRELENTING", "VEILPIERCER", "VICIOUS", "WELL_LAID_PLANS", "WRAITH_FORM"
    };

    public static void Test_Content_cards_2_3_EveryPortedIdIsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.False(def is UnportedCardDef, $"{id} resolves to its ported def");
            Assert.Equal(id, def.Id, $"{id} id literal");
            def.CreateVars();
        }
    }

    public static void Test_Content_cards_2_3_UpgradesFollowTheDecompile()
    {
        var relax = new CardInstance(CardRegistry.Get(Relax.CardId), 1);
        relax.Upgrade();
        Assert.Equal(18m, relax.Vars.Block.BaseValue, "Relax+ block");
        Assert.Equal(3m, relax.Vars.Cards.BaseValue, "Relax+ cards");
        Assert.Equal(3m, relax.Vars.Energy.BaseValue, "Relax+ energy");

        var wraithForm = new CardInstance(CardRegistry.Get(WraithForm.CardId), 2);
        wraithForm.Upgrade();
        Assert.Equal(3m, wraithForm.Vars[WraithForm.IntangibleVarName].BaseValue, "Wraith Form+ intangible");
        Assert.Equal(1m, wraithForm.Vars[WraithForm.WraithFormVarName].BaseValue, "Wraith Form+ dexterity loss unchanged");

        var prolong = new CardInstance(CardRegistry.Get(Prolong.CardId), 3);
        Assert.True(prolong.HasKeyword(CardKeyword.Exhaust), "Prolong exhausts");
        prolong.Upgrade();
        Assert.False(prolong.HasKeyword(CardKeyword.Exhaust), "Prolong+ loses Exhaust");

        var speedster = new CardInstance(CardRegistry.Get(Speedster.CardId), 4);
        speedster.Upgrade();
        Assert.True(speedster.HasKeyword(CardKeyword.Innate), "Speedster+ gains Innate");

        var shadowmeld = new CardInstance(CardRegistry.Get(Shadowmeld.CardId), 5);
        shadowmeld.Upgrade();
        Assert.Equal(0, shadowmeld.EnergyCost.Base, "Shadowmeld+ cost");
    }
}
