using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Cards.Afflictions;
using Sts2Sim.Core.Cards.Enchantments;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_orbs_ench_afflTests
{
    private static readonly string[] OrbIds =
    {
        "DARK_ORB", "FROST_ORB", "GLASS_ORB", "LIGHTNING_ORB", "PLASMA_ORB"
    };

    private static readonly string[] EnchantmentIds =
    {
        "CLONE", "CORRUPTED", "GLAM", "INKY", "INSTINCT", "MOMENTUM", "NIMBLE", "ROYALLY_APPROVED",
        "SHARP", "SOULS_POWER", "SOWN", "SPIRAL", "STEADY", "SWIFT", "TEZCATARAS_EMBER", "VIGOROUS",
        "ADROIT", "GOOPY", "IMBUED", "PERFECT_FIT", "SLITHER"
    };

    private static readonly string[] AfflictionIds =
    {
        "BOUND", "ENTANGLED", "GALVANIZED", "HEXED", "RINGING", "SMOG", "TAINTED"
    };

    public static void Test_ContentOrbsEnchAffl_AllIdsRegistered()
    {
        foreach (var id in OrbIds)
        {
            Assert.True(OrbRegistry.Models.Contains(id), id);
            Assert.Equal(id, OrbRegistry.Create(id).Id);
        }
        foreach (var id in EnchantmentIds)
        {
            Assert.True(EnchantmentRegistry.Models.Contains(id), id);
            Assert.Equal(id, EnchantmentRegistry.Create(id).Id);
        }
        foreach (var id in AfflictionIds)
        {
            Assert.True(AfflictionRegistry.Models.Contains(id), id);
            Assert.Equal(id, AfflictionRegistry.Create(id).Id);
        }
    }

    public static void Test_ContentOrbsEnchAffl_EnchantEligibility()
    {
        var cs = NewCombat();
        var strike = MakeDeck(cs, StrikeIronclad.CardId);
        var defend = MakeDeck(cs, DefendIronclad.CardId);
        Assert.True(new Spiral().CanEnchant(strike), "Spiral takes a Basic Strike");
        Assert.True(new Goopy().CanEnchant(defend), "Goopy takes a Defend");
        Assert.False(new Goopy().CanEnchant(strike), "Goopy needs the Defend tag");
        Assert.False(new Sharp().CanEnchant(defend), "Sharp is Attack only");
        Assert.True(new RoyallyApproved().CanEnchant(defend));
        Assert.False(new Tainted().CanAfflict(cs, strike), "Tainted is Skill only");
        Assert.True(new Tainted().CanAfflict(cs, defend));
    }

    public static void Test_ContentOrbsEnchAffl_TezcatarasEmberAndMomentum()
    {
        var cs = NewCombat();
        var strike = MakeDeck(cs, StrikeIronclad.CardId);
        CardCmd.Enchant(new TezcatarasEmber(), strike, 1m);
        Assert.Equal(0, strike.EnergyCost.GetWithModifiers(CostModifiers.None), "Tezcatara's Ember zeroes the cost");
        Assert.True(strike.HasKeyword(CardKeyword.Eternal));
        Assert.Equal(3m, strike.Enchantment!.EnchantDamageAdditive(6m, ValueProp.Move));
        Assert.Equal(0m, strike.Enchantment!.EnchantDamageAdditive(6m, ValueProp.Unpowered));

        var other = MakeDeck(cs, StrikeIronclad.CardId);
        var momentum = (Momentum)Assert.NotNull(CardCmd.Enchant(new Momentum(), other, 2m));
        Assert.Equal(0m, momentum.EnchantDamageAdditive(6m, ValueProp.Move));
        momentum.OnPlay(cs, null);
        momentum.OnPlay(cs, null);
        Assert.Equal(4m, momentum.EnchantDamageAdditive(6m, ValueProp.Move), "Momentum grows by Amount per play");
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = 3
        };
        var cs = new CombatState(player, "CONTENT_ORBS_ENCH_AFFL_TESTS", 0, new RunRngSet(7UL));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static CardInstance MakeDeck(CombatState cs, string id)
    {
        var card = new CardInstance(CardRegistry.Get(id), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        cs.Player.Deck.Add(card);
        return card;
    }
}
