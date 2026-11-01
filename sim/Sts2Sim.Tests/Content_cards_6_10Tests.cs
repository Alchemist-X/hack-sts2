using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_6_10Tests
{
    private static readonly (string Id, Type Type)[] Ported =
    {
        ("ABUNDANCE", typeof(Abundance)),
        ("BUNDLE_OF_JOY", typeof(BundleOfJoy)),
        ("CALAMITY", typeof(Calamity)),
        ("CALL_OF_THE_VOID", typeof(CallOfTheVoid)),
        ("DISTRACTION", typeof(Distraction)),
        ("HELLO_WORLD", typeof(HelloWorld)),
        ("INFERNAL_BLADE", typeof(InfernalBlade)),
        ("JACKPOT", typeof(Jackpot)),
        ("JACK_OF_ALL_TRADES", typeof(JackOfAllTrades)),
        ("MANIFEST_AUTHORITY", typeof(ManifestAuthority)),
        ("METAMORPHOSIS", typeof(Metamorphosis)),
        ("SPECTRUM_SHIFT", typeof(SpectrumShift)),
        ("SPLASH", typeof(Splash)),
        ("STOKE", typeof(Stoke)),
        ("ALCHEMIZE", typeof(Alchemize)),
        ("BLADE_OF_INK", typeof(BladeOfInk)),
        ("DEBT", typeof(Debt)),
        ("DISINTEGRATION", typeof(Disintegration)),
        ("HAND_OF_GREED", typeof(HandOfGreed)),
        ("MIND_ROT", typeof(MindRot)),
        ("SLOTH", typeof(Sloth)),
        ("WASTE_AWAY", typeof(WasteAway)),
    };

    public static void Test_Content_cards_6_10_AllPortedIdsRegistered()
    {
        foreach (var (id, type) in Ported)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id + " registered");
            Assert.True(def.GetType() == type, id + " resolves to " + type.Name);
            Assert.Equal(id, DeterministicHash.Slugify(type.Name));
        }
    }

    public static void Test_Content_cards_6_10_DebtLosesUpToTenGoldAtTurnEndInHand()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var cs = new CombatState(player, "CONTENT_CARDS_6_10", 0, new RunRngSet(7UL));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        var debt = cs.CreateCard(CardRegistry.Get(Debt.CardId), cs.Player);
        cs.Player.Hand.AddInternal(debt);
        cs.Player.Gold = 25;
        debt.OnTurnEndInHand(cs);
        Assert.Equal(15, cs.Player.Gold);
        cs.Player.Gold = 4;
        debt.OnTurnEndInHand(cs);
        Assert.Equal(0, cs.Player.Gold);
    }
}
