using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Cards.Afflictions;
using Sts2Sim.Core.Cards.Enchantments;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class CardKernelTests
{
    private const string ChoiceArea = "choice (CardSelectCmd)";
    private const string HooksArea = "hooks (Hook dispatchers and card listeners)";

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_CardKernel_CloneForCombatKeepsOwnerResetsPlayStateAndLinksTheDeckCard()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var deckCard = MakeDeck(cs, Bash.CardId);
        deckCard.Upgrade();
        deckCard.AddKeyword(CardKeyword.Retain);
        deckCard.EnergyCost.SetThisCombat(1);
        deckCard.SetStarCostThisCombat(2);
        deckCard.BaseReplayCount = 2;
        deckCard.ExhaustOnNextPlay = true;
        deckCard.HasSingleTurnRetain = true;
        deckCard.LastStarsSpent = 4;
        deckCard.CurrentTargetId = 1;
        deckCard.CurrentPlayIndex = 2;
        deckCard.HasBeenRemovedFromState = true;
        deckCard.DeckVersionId = 77;

        int newId = cs.AllocateCardInstanceId();
        var copy = deckCard.CloneForCombat(newId);

        Assert.True(ReferenceEquals(cs.Player, copy.Owner), "the copy keeps the deck card's owner");
        Assert.Equal(newId, copy.InstanceId);
        Assert.Equal(deckCard.InstanceId, copy.DeckVersionId, "DeckVersionId links the copy to its deck card");
        Assert.Equal(CardInstance.NoTarget, copy.CurrentTargetId);
        Assert.Equal(0, copy.CurrentPlayIndex);
        Assert.False(copy.HasBeenRemovedFromState);
        Assert.Equal(1, copy.UpgradeLevel);
        Assert.True(copy.HasKeyword(CardKeyword.Retain));
        Assert.Equal(1, copy.EnergyCost.GetResolved());
        Assert.Equal(2, copy.CurrentStarCost);
        Assert.Equal(2, copy.BaseReplayCount);
        Assert.True(copy.ExhaustOnNextPlay);
        Assert.True(copy.HasSingleTurnRetain);
        Assert.Equal(4, copy.LastStarsSpent);
        Assert.Equal(Bash.UpgradedDamage, copy.Vars.Damage.BaseValue);
        Assert.False(ReferenceEquals(deckCard.Vars, copy.Vars));
        Assert.False(ReferenceEquals(deckCard.EnergyCost, copy.EnergyCost));
        Assert.False(ReferenceEquals(deckCard.Vars.Damage, copy.Vars.Damage));
        Assert.Equal(0, cs.Player.DrawPile.Count, "CloneForCombat adds no pile entry");
        Assert.Equal(1, cs.Player.Deck.Count);

        ulong deckHash = StateHasher.Hash(deckCard);
        copy.Vars.Damage.BaseValue = 99m;
        copy.EnergyCost.AddThisCombat(1);
        copy.SetStarCostThisTurn(0);
        copy.RemoveKeyword(CardKeyword.Retain);
        Assert.Equal(deckHash, StateHasher.Hash(deckCard), "the deck card is independent of its copy");
        Assert.Equal(Bash.UpgradedDamage, deckCard.Vars.Damage.BaseValue);
        Assert.True(deckCard.HasKeyword(CardKeyword.Retain));
        Assert.Equal(2, deckCard.CurrentStarCost);

        var ownerless = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), 500);
        var ownerlessCopy = ownerless.CloneForCombat(501);
        Assert.False(ownerlessCopy.HasOwner, "an ownerless card stays ownerless");
        Assert.Equal(500, ownerlessCopy.DeckVersionId);
    }

    public static void Test_CardKernel_CloneForCombatRebindsTheEnchantmentAndTheAffliction()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var deckCard = MakeDeck(cs, StrikeIronclad.CardId);
        var glam = (Glam)Assert.NotNull(CardCmd.Enchant(new Glam(), deckCard, 2m));
        glam.Status = EnchantmentStatus.Disabled;
        glam.DynamicVars[Glam.TimesKey].BaseValue = 3m;
        var log = new List<string>();
        deckCard.AfflictInternal(new RecordingAffliction(log), 3m);

        var copy = deckCard.CloneForCombat(cs.AllocateCardInstanceId());
        var copyGlam = (Glam)Assert.NotNull(copy.Enchantment);
        var copyAffliction = Assert.NotNull(copy.Affliction);
        Assert.False(ReferenceEquals(glam, copyGlam), "the enchantment is cloned");
        Assert.True(ReferenceEquals(copy, copyGlam.Card), "the cloned enchantment is bound to the copy");
        Assert.True(ReferenceEquals(deckCard, glam.Card), "the deck card keeps its own enchantment");
        Assert.Equal(2, copyGlam.Amount);
        Assert.Equal(EnchantmentStatus.Disabled, copyGlam.Status);
        Assert.Equal(3m, copyGlam.DynamicVars[Glam.TimesKey].BaseValue);
        Assert.False(ReferenceEquals(glam.DynamicVars, copyGlam.DynamicVars));
        Assert.False(ReferenceEquals(deckCard.Affliction, copyAffliction));
        Assert.True(ReferenceEquals(copy, copyAffliction.Card));
        Assert.Equal(3, copyAffliction.Amount);
        Assert.Equal(0, log.Count, "re-binding runs neither AfterApplied nor BeforeRemoved");

        copyGlam.DynamicVars[Glam.TimesKey].BaseValue = 9m;
        copyAffliction.Amount = 5;
        Assert.Equal(3m, glam.DynamicVars[Glam.TimesKey].BaseValue);
        Assert.Equal(3, Assert.NotNull(deckCard.Affliction).Amount);
    }

    public static void Test_CardKernel_CloneForCombatHashEqualsAnIndependentlyBuiltCopy()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var deckCard = MakeDeck(cs, DefendIronclad.CardId);
        deckCard.Upgrade();
        deckCard.EnergyCost.SetThisCombat(0);
        deckCard.SetStarCostThisCombat(1);
        deckCard.AddKeyword(CardKeyword.Innate);
        CardCmd.Enchant(new Glam(), deckCard, 1m);
        int newId = cs.AllocateCardInstanceId();
        var copy = deckCard.CloneForCombat(newId);

        var built = new CardInstance(CardRegistry.Get(DefendIronclad.CardId), newId);
        built.AttachTo(cs.Player);
        built.Upgrade();
        built.EnergyCost.SetThisCombat(0);
        built.SetStarCostThisCombat(1);
        built.AddKeyword(CardKeyword.Innate);
        built.EnchantInternal(new Glam(), 1m);
        built.DeckVersionId = deckCard.InstanceId;
        Assert.SequenceEqual(StateHasher.CanonicalBytes(built), StateHasher.CanonicalBytes(copy), "a combat copy equals a card built from scratch with the same state");
        Assert.Equal(StateHasher.Hash(built), StateHasher.Hash(copy));
        Assert.Equal(StateHasher.Hash(copy), StateHasher.Hash(copy.CloneFor(cs.Player)), "Hash(x) == Hash(Clone(x))");
    }

    public static void Test_CardKernel_CloneForCombatReinitialisesCalculatedVarsWhileDeepCloneCopiesExactly()
    {
        var cs = NewCombat();
        var card = new CardInstance(new CalcCard(), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        Assert.Equal(CalcCard.Base, card.Vars.CalculatedDamage.BaseValue, "creation initialises the calculated var from its base var");
        card.Vars.CalculationBase.BaseValue = 11m;
        Assert.Equal(CalcCard.Base, card.Vars.CalculatedDamage.BaseValue, "a direct base write leaves the calculated var stale");
        Assert.Equal(CalcCard.Base, card.DeepClone().Vars.CalculatedDamage.BaseValue, "DeepClone is an exact snapshot");
        Assert.Equal(11m, card.CloneForCombat(cs.AllocateCardInstanceId()).Vars.CalculatedDamage.BaseValue, "CloneForCombat re-initialises like DynamicVarSet.Clone(owner)");
    }

    public static void Test_CardKernel_OwnerIsSetByEveryCreationPathAndAttachToIsStrict()
    {
        var cs = NewCombat();
        var created = cs.CreateCard(CardRegistry.Get(StrikeIronclad.CardId), cs.Player);
        Assert.True(created.HasOwner);
        Assert.True(ReferenceEquals(cs.Player, created.Owner));
        Assert.Throws<InvalidOperationException>(() => created.AttachTo(cs.Player), "a second attach throws even for the same owner");
        var other = new PlayerState(9, StartingDecks.IroncladCharacterId, 50, 50, 3);
        Assert.Throws<InvalidOperationException>(() => created.AttachTo(other));

        var loose = CardInstance.Create(cs, StrikeIronclad.CardId);
        Assert.False(loose.HasOwner, "CardInstance.Create is the ownerless ToMutable step");
        Assert.Throws<InvalidOperationException>(() => _ = loose.Owner);
        loose.AttachTo(cs.Player);
        Assert.True(ReferenceEquals(cs.Player, loose.Owner));

        var deckCard = MakeDeck(cs, StrikeIronclad.CardId);
        Assert.True(ReferenceEquals(cs.Player, deckCard.CloneForCombat(cs.AllocateCardInstanceId()).Owner));
        Assert.True(ReferenceEquals(other, created.CloneFor(other).Owner), "a state clone rebinds the owner");
        Assert.True(ReferenceEquals(cs.Player, created.DeepClone().Owner), "DeepClone keeps the owner reference");
        Assert.False(new CardInstance(CardRegistry.Get(Bash.CardId), 3).CloneFor(other).HasOwner, "an ownerless card stays ownerless under CloneFor");

        var slimeCs = NewCombat();
        AddMonster(slimeCs);
        Slimed.AddToCombatAndPreview(slimeCs, new CreatureState[] { slimeCs.Player }, PileType.Discard, 2, null);
        Assert.Equal(2, slimeCs.Player.DiscardPile.Count);
        foreach (var slimed in slimeCs.Player.DiscardPile.Cards)
        {
            Assert.True(ReferenceEquals(slimeCs.Player, slimed.Owner), "generated status cards are owned by the target player");
        }
    }

    public static void Test_CardKernel_ArmamentsPlusUpgradesTheCombatCopiesAndLeavesTheDeckUnchanged()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var deckArmaments = MakeDeck(cs, Armaments.CardId);
        var deckStrike = MakeDeck(cs, StrikeIronclad.CardId);
        var deckDefend = MakeDeck(cs, DefendIronclad.CardId);
        var deckBash = MakeDeck(cs, Bash.CardId);
        var armaments = deckArmaments.CloneForCombat(cs.AllocateCardInstanceId());
        armaments.Upgrade();
        cs.Player.PlayPile.AddInternal(armaments);
        var copies = new[] { deckStrike, deckDefend, deckBash }.Select(d => d.CloneForCombat(cs.AllocateCardInstanceId())).ToList();
        foreach (var copy in copies)
        {
            cs.Player.Hand.AddInternal(copy);
        }

        armaments.OnPlay(cs, new CardPlay(cs, armaments, null));

        Assert.Equal((int)Armaments.BaseBlock, cs.Player.Block);
        Assert.True(copies.All(c => c.IsUpgraded), "Armaments+ upgrades every upgradable card in hand");
        Assert.Equal(StrikeIronclad.UpgradedDamage, copies[0].Vars.Damage.BaseValue);
        Assert.Equal(DefendIronclad.UpgradedBlock, copies[1].Vars.Block.BaseValue);
        Assert.Equal(Bash.UpgradedVulnerable, copies[2].Vars.Vulnerable.BaseValue);
        Assert.True(copies.All(c => !c.Vars.All.Any(v => v.WasJustUpgraded)), "CardCmd.Upgrade finalizes each upgrade");
        foreach (var deckCard in new[] { deckArmaments, deckStrike, deckDefend, deckBash })
        {
            Assert.False(deckCard.IsUpgraded, $"{deckCard} in the deck stays unupgraded");
        }
        Assert.Equal(StrikeIronclad.BaseDamage, deckStrike.Vars.Damage.BaseValue);
        Assert.Equal(DefendIronclad.BaseBlock, deckDefend.Vars.Block.BaseValue);
        Assert.Equal(Bash.BaseDamage, deckBash.Vars.Damage.BaseValue);
    }

    public static void Test_CardKernel_ArmamentsUpgradesTheOnlyUpgradableHandCard()
    {
        W1Pending.Run(nameof(Test_CardKernel_ArmamentsUpgradesTheOnlyUpgradableHandCard), ChoiceArea, () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            var deckStrike = MakeDeck(cs, StrikeIronclad.CardId);
            var strike = deckStrike.CloneForCombat(cs.AllocateCardInstanceId());
            cs.Player.Hand.AddInternal(strike);
            cs.Player.Hand.AddInternal(Make(cs, Greed.CardId));
            var armaments = MakeIn(cs, PileType.Play, Armaments.CardId);
            armaments.OnPlay(cs, new CardPlay(cs, armaments, null));
            Assert.Equal((int)Armaments.BaseBlock, cs.Player.Block);
            Assert.True(strike.IsUpgraded, "the single upgradable hand card is upgraded without a prompt");
            Assert.False(deckStrike.IsUpgraded, "the deck card stays unupgraded");
        });
    }

    public static void Test_CardKernel_UpgradeHonoursEndingAndUpgradability()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs);
        var greed = MakeIn(cs, PileType.Hand, Greed.CardId);
        CardCmd.Upgrade(cs, greed);
        Assert.Equal(0, greed.UpgradeLevel, "a card that cannot be upgraded is skipped");
        var strike = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        CardCmd.Upgrade(cs, strike);
        Assert.True(strike.IsUpgraded);
        CardCmd.Upgrade(cs, strike);
        Assert.Equal(1, strike.UpgradeLevel, "a second upgrade is skipped at MaxUpgradeLevel");

        var bash = MakeIn(cs, PileType.Hand, Bash.CardId);
        monster.CurrentHp = 0;
        Assert.True(CombatStatus.IsEnding(cs));
        CardCmd.Upgrade(cs, new[] { bash });
        Assert.False(bash.IsUpgraded, "CardCmd.Upgrade returns while the combat is ending");
        CardCmd.Downgrade(cs, strike);
        Assert.True(strike.IsUpgraded, "CardCmd.Downgrade returns while the combat is ending");

        var deckCard = MakeDeck(cs, Bash.CardId);
        CardCmd.Upgrade(null, deckCard);
        Assert.True(deckCard.IsUpgraded, "outside a combat a deck card is upgraded in place");
    }

    public static void Test_CardKernel_UpgradeInternalRecalculatesCalculatedVarsAndFinalizeClearsTheFlags()
    {
        var cs = NewCombat();
        var card = new CardInstance(new CalcCard(), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        card.UpgradeInternal();
        Assert.Equal(CalcCard.Base + CalcCard.BaseUpgrade, card.Vars.CalculationBase.BaseValue);
        Assert.Equal(CalcCard.Base + CalcCard.BaseUpgrade, card.Vars.CalculatedDamage.BaseValue, "RecalculateForUpgradeOrEnchant copies the base var");
        Assert.True(card.Vars.CalculatedDamage.WasJustUpgraded, "a changed base marks the calculated var as just upgraded");
        card.FinalizeUpgradeInternal();
        Assert.False(card.Vars.All.Any(v => v.WasJustUpgraded));
        Assert.Throws<InvalidOperationException>(() => card.UpgradeInternal(), "UpgradeInternal past MaxUpgradeLevel throws");
    }

    public static void Test_CardKernel_DowngradeRestoresTheCanonicalCardAndRerunsTheModifiers()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var strike = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var log = new List<string>();
        var enchantment = new RecordingEnchantment(log);
        CardCmd.Enchant(enchantment, strike, 1m);
        strike.AfflictInternal(new RecordingAffliction(log), 1m);
        strike.Upgrade();
        strike.AddKeyword(CardKeyword.Exhaust);
        strike.EnergyCost.UpgradeBy(1);
        log.Clear();

        CardCmd.Downgrade(cs, strike);
        Assert.Equal(0, strike.UpgradeLevel);
        Assert.Equal(StrikeIronclad.BaseDamage, strike.Vars.Damage.BaseValue);
        Assert.Equal(StrikeIronclad.Cost, strike.EnergyCost.Base);
        Assert.False(strike.HasKeyword(CardKeyword.Exhaust), "local keywords return to the canonical set");
        Assert.SequenceEqual(new[] { "OnEnchant", "RecalculateValues", "AfterApplied" }, log, "Enchantment.ModifyCard then Affliction.AfterApplied");

        var blade = MakeIn(cs, PileType.Hand, SovereignBlade.CardId);
        var bladeDef = (SovereignBlade)blade.Def;
        bladeDef.AddDamage(blade, 5m);
        bladeDef.SetRepeats(blade, 3m);
        blade.Upgrade();
        CardCmd.Downgrade(cs, blade);
        Assert.Equal(SovereignBlade.BaseDamage + 5m, blade.Vars.Damage.BaseValue, "AfterDowngraded restores the forged damage");
        Assert.Equal(3m, blade.Vars.Repeat.BaseValue);
        Assert.Equal(SovereignBlade.Cost, blade.EnergyCost.Base);
    }

    public static void Test_CardKernel_TransformToKeepsThePileSlotAndRemovesTheOriginal()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var a = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var b = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        var c = MakeIn(cs, PileType.Hand, Bash.CardId);
        int nextId = cs.NextCardInstanceId;
        int generated = cs.History.CardGeneratedEntries.Count;

        var result = CardCmd.TransformTo(cs, b, Soul.CardId);
        Assert.True(result.HasValue);
        var added = result!.Value.CardAdded;
        Assert.True(result.Value.Success);
        Assert.Equal(Soul.CardId, added.Id);
        Assert.Equal(nextId, added.InstanceId, "the replacement is created before the pile edit");
        Assert.SequenceEqual(new[] { a, added, c }, cs.Player.Hand.Cards, "the replacement takes the original's slot");
        Assert.True(b.HasBeenRemovedFromState);
        Assert.Null(cs.Player.FindPileContaining(b));
        Assert.True(ReferenceEquals(cs.Player, added.Owner));
        Assert.Equal(generated + 1, cs.History.CardGeneratedEntries.Count);
        Assert.Equal(added.InstanceId, cs.History.CardGeneratedEntries[^1].CardInstanceId);
    }

    public static void Test_CardKernel_TransformBatchSortsByPileThenIndex()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var h0 = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var h1 = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        var h2 = MakeIn(cs, PileType.Hand, Bash.CardId);
        var d0 = MakeIn(cs, PileType.Discard, StrikeIronclad.CardId);
        var d1 = MakeIn(cs, PileType.Discard, DefendIronclad.CardId);
        var soul = CardRegistry.Get(Soul.CardId);
        var rd1 = cs.CreateCard(soul, cs.Player);
        var rh2 = cs.CreateCard(soul, cs.Player);
        var rh0 = cs.CreateCard(soul, cs.Player);
        var results = CardCmd.Transform(cs, new[]
        {
            new CardTransformation(cs, d1, rd1),
            new CardTransformation(cs, h2, rh2),
            new CardTransformation(cs, h0, rh0)
        }, null);
        Assert.SequenceEqual(new[] { rh0, rh2, rd1 }, results.Select(r => r.CardAdded), "results follow (pile type, index) order");
        Assert.SequenceEqual(new[] { rh0, h1, rh2 }, cs.Player.Hand.Cards);
        Assert.SequenceEqual(new[] { d0, rd1 }, cs.Player.DiscardPile.Cards);
        Assert.True(d1.HasBeenRemovedFromState && h2.HasBeenRemovedFromState && h0.HasBeenRemovedFromState);
        Assert.Equal(0, CardCmd.Transform(cs, Array.Empty<CardTransformation>(), null).Count);
    }

    public static void Test_CardKernel_TransformToRandomDrawsFromTheGivenStreamOverThePortedOptions()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var strike = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var stream = cs.Rng.CombatCardSelection;
        var reference = stream.DeepClone();
        int before = stream.Counter;
        var expectedOptions = CardPools.Ironclad.GetUnlockedCards(UnlockProfile.All, CardMultiplayerConstraint.SingleplayerOnly)
            .Where(d => (uint)(d.Rarity - CardRarity.Common) <= 2u && d.CanBeGeneratedInCombat && d.Id != StrikeIronclad.CardId)
            .ToList();
        var ported = expectedOptions.Where(d => d is not UnportedCardDef).ToList();
        var expected = Assert.NotNull(reference.NextItem((IReadOnlyList<CardDef>)ported));

        var result = CardCmd.TransformToRandom(cs, strike, stream);
        Assert.Equal(expected.Id, result.CardAdded.Id, "NextItem over the ported options of the original's pool");
        Assert.Equal(before + 1, stream.Counter, "one draw on the caller's stream");
        Assert.True(ReferenceEquals(result.CardAdded, cs.Player.Hand[0]));
        Assert.True(strike.HasBeenRemovedFromState);
        int unported = expectedOptions.Count - ported.Count;
        Assert.True(unported > 0);
        var flag = cs.Run.Fidelity.Entries.Single();
        Assert.Equal(FidelityFlag.ApproximatedRule, flag.Flag);
        Assert.True(flag.Site.StartsWith($"CardFactory.CreateRandomCardForTransform: {unported} of {expectedOptions.Count} options", StringComparison.Ordinal), flag.Site);
    }

    public static void Test_CardKernel_EternalCardsAreTransformableOnlyOutsideTheDeck()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var bane = MakeIn(cs, PileType.Hand, AscendersBane.CardId);
        Assert.False(bane.IsRemovableIn(cs));
        Assert.True(bane.IsTransformable(cs), "an Eternal card in a combat pile is transformable");
        var deckBane = MakeDeck(cs, AscendersBane.CardId);
        Assert.False(deckBane.IsTransformable(cs), "an Eternal deck card is not");
        Assert.Throws<InvalidOperationException>(() => _ = new CardTransformation(cs, deckBane));
        var loose = Make(cs, StrikeIronclad.CardId);
        Assert.Throws<InvalidOperationException>(() => CardCmd.Transform(cs, new[] { new CardTransformation(cs, loose, Make(cs, Soul.CardId)) }, null), "a card in no pile cannot be transformed");
        var deckStrike = MakeDeck(cs, StrikeIronclad.CardId);
        Assert.Throws<NotImplementedException>(() => CardCmd.TransformTo(cs, deckStrike, Soul.CardId), "deck transforms are run-layer work");
    }

    public static void Test_CardKernel_EnchantmentBaseVirtualsAreIdentityAndCanEnchantFollowsTheGame()
    {
        var plain = new PlainEnchantment();
        Assert.Equal(0m, plain.EnchantBlockAdditive(7m));
        Assert.Equal(1m, plain.EnchantBlockMultiplicative(7m));
        Assert.Equal(0m, plain.EnchantDamageAdditive(7m, ValueProp.Move));
        Assert.Equal(1m, plain.EnchantDamageMultiplicative(7m, ValueProp.Move));
        Assert.Equal(4, plain.EnchantPlayCount(4));
        Assert.False(plain.ShouldStartAtBottomOfDrawPile);
        Assert.False(plain.IsStackable);
        Assert.True(plain.CanEnchantCardType(CardType.Curse));
        Assert.Null(plain.Card, "the Card getter is null until the enchantment is applied");
        Assert.False(plain.HasCard);
        Assert.Equal(0, plain.DynamicVars.Count);

        var cs = NewCombat();
        AddMonster(cs);
        Assert.False(new PlainEnchantment().CanEnchant(MakeIn(cs, PileType.Hand, Slimed.CardId)), "Status cards are rejected");
        Assert.False(new PlainEnchantment().CanEnchant(MakeIn(cs, PileType.Hand, Greed.CardId)), "Curse cards are rejected");
        var log = new List<string>();
        Assert.False(new RecordingEnchantment(log, only: CardType.Skill).CanEnchant(MakeIn(cs, PileType.Hand, StrikeIronclad.CardId)), "CanEnchantCardType gates the type");
        var unplayableDeck = MakeDeck(cs, StrikeIronclad.CardId);
        unplayableDeck.AddKeyword(CardKeyword.Unplayable);
        Assert.False(new PlainEnchantment().CanEnchant(unplayableDeck), "an Unplayable deck card is rejected");
        var unplayableHand = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        unplayableHand.AddKeyword(CardKeyword.Unplayable);
        Assert.True(new PlainEnchantment().CanEnchant(unplayableHand), "an Unplayable combat card is accepted");
        var enchanted = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        CardCmd.Enchant(new RecordingEnchantment(log, stackable: true), enchanted, 1m);
        Assert.True(new RecordingEnchantment(log, stackable: true).CanEnchant(enchanted), "a stackable enchantment of the same type is accepted");
        Assert.False(new PlainEnchantment().CanEnchant(enchanted), "another enchantment type is rejected");
        Assert.False(new RecordingEnchantment(log).CanEnchant(MakeIn(cs, PileType.Hand, StrikeIronclad.CardId).Also(c => CardCmd.Enchant(new RecordingEnchantment(log), c, 1m))), "a non-stackable enchantment rejects a second copy");
    }

    public static void Test_CardKernel_EnchantAppliesTruncatesModifiesFinalizesAndStacks()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Hand, new CalcCard());
        card.Vars.Damage.UpgradeValueBy(0m);
        var log = new List<string>();
        var enchantment = new RecordingEnchantment(log, stackable: true);
        card.Vars.CalculationBase.BaseValue = 6m;
        var applied = CardCmd.Enchant(enchantment, card, 2.9m);
        Assert.True(ReferenceEquals(enchantment, applied));
        Assert.True(ReferenceEquals(card, enchantment.Card));
        Assert.Equal(2, enchantment.Amount, "ApplyInternal truncates the amount");
        Assert.SequenceEqual(new[] { "OnEnchant", "RecalculateValues" }, log);
        Assert.Equal(6m, card.Vars.CalculatedDamage.BaseValue, "ModifyCard ends with the card's RecalculateForUpgradeOrEnchant");
        Assert.False(card.Vars.All.Any(v => v.WasJustUpgraded), "CardCmd.Enchant finalizes the card");
        Assert.Throws<InvalidOperationException>(() => enchantment.ApplyInternal(MakeIn(cs, PileType.Hand, StrikeIronclad.CardId), 1m), "an applied enchantment cannot be applied again");

        log.Clear();
        var second = new RecordingEnchantment(log, stackable: true);
        Assert.True(ReferenceEquals(enchantment, CardCmd.Enchant(second, card, 3.7m)), "stacking returns the card's existing enchantment");
        Assert.Equal(5, enchantment.Amount);
        Assert.Equal(0, log.Count, "stacking runs no ModifyCard");
        Assert.False(second.HasCard);
        Assert.Throws<InvalidOperationException>(() => CardCmd.Enchant(new PlainEnchantment(), card, 1m), "CanEnchant false throws");

        CardCmd.ClearEnchantment(card);
        Assert.Null(card.Enchantment);
        Assert.False(enchantment.HasCard, "ClearInternal unbinds the model");
        CardCmd.ClearEnchantment(card);
        var byId = CardCmd.Enchant(Glam.EnchantmentId, card, 1m);
        Assert.True(byId is Glam && ReferenceEquals(card, byId.Card), "a registry enchantment applies by id");
    }

    public static void Test_CardKernel_EnchantedReplayCountReadsTheEnchantment()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        Assert.Equal(0, card.GetEnchantedReplayCount());
        card.BaseReplayCount = 2;
        Assert.Equal(2, card.GetEnchantedReplayCount());
        var glam = (Glam)Assert.NotNull(CardCmd.Enchant(new Glam(), card, 1m));
        Assert.Equal(3, card.GetEnchantedReplayCount(), "Glam adds Times plays until it is used");
        var play = new CardPlay(cs, card, null);
        glam.AfterCardPlayed(cs, new CardPlay(cs, MakeIn(cs, PileType.Hand, DefendIronclad.CardId), null));
        Assert.False(glam.UsedThisCombat, "another card's play does not use Glam");
        glam.AfterCardPlayed(cs, play);
        Assert.True(glam.UsedThisCombat);
        Assert.Equal(EnchantmentStatus.Disabled, glam.Status);
        Assert.Equal(2, card.GetEnchantedReplayCount());
        var plain = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        CardCmd.Enchant(new ReplayEnchantment(), plain, 1m);
        Assert.Equal(5, plain.GetEnchantedReplayCount(), "EnchantPlayCount receives BaseReplayCount");
    }

    public static void Test_CardKernel_AfflictionBaseRulesAndTheAmountSetterRecalculatesCardValues()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var log = new List<string>();
        var attack = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var skill = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        var plain = new RecordingAffliction(log);
        Assert.Null(plain.Card);
        Assert.True(plain.CanAfflictUnplayableCards);
        Assert.False(plain.IsStackable);
        Assert.True(plain.CanAfflict(cs, attack));
        Assert.False(new RecordingAffliction(log, only: CardType.Skill).CanAfflict(cs, attack));
        Assert.True(new RecordingAffliction(log, only: CardType.Skill).CanAfflict(cs, skill));
        var keywords = Give(cs, new AddKeywordPower(CardKeyword.Unplayable));
        Assert.False(new RecordingAffliction(log, unplayable: false).CanAfflict(cs, attack), "the in-combat Unplayable keyword gates CanAfflictUnplayableCards");
        keywords.RemoveInternal();
        Assert.True(new RecordingAffliction(log, unplayable: false).CanAfflict(cs, attack));

        var watcher = new RecordingEnchantment(log);
        CardCmd.Enchant(watcher, skill, 1m);
        log.Clear();
        attack.AfflictInternal(plain, 2m);
        Assert.True(ReferenceEquals(attack, plain.Card));
        Assert.Equal(2, plain.Amount);
        Assert.SequenceEqual(new[] { "RecalculateValues" }, log, "the Amount setter recalculates the owner's enchanted cards");
        log.Clear();
        plain.Amount = 2;
        Assert.Equal(0, log.Count, "an unchanged amount recalculates nothing");
        plain.Amount = 4;
        Assert.SequenceEqual(new[] { "RecalculateValues" }, log);
        Assert.Throws<InvalidOperationException>(() => attack.AfflictInternal(new RecordingAffliction(log), 1m), "a second affliction throws");
        Assert.False(new RecordingAffliction(log).CanAfflict(cs, attack), "a non-stackable affliction rejects an afflicted card");
        Assert.True(new RecordingAffliction(log, stackable: true).CanAfflict(cs, MakeIn(cs, PileType.Hand, Bash.CardId).Also(c => c.AfflictInternal(new RecordingAffliction(log, stackable: true), 1m))));

        log.Clear();
        CardCmd.ClearAffliction(attack);
        Assert.Null(attack.Affliction);
        Assert.False(plain.HasCard);
        Assert.SequenceEqual(new[] { "BeforeRemoved", "RecalculateValues" }, log, "ClearInternal then the owner's RecalculateCardValues");
    }

    public static void Test_CardKernel_AfflictStacksAndRecordsHistory()
    {
        W1Pending.Run(nameof(Test_CardKernel_AfflictStacksAndRecordsHistory), HooksArea + ": Hook.ShouldAfflict", () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            var log = new List<string>();
            var card = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
            var first = new RecordingAffliction(log, stackable: true);
            Assert.True(ReferenceEquals(first, CardCmd.Afflict(cs, first, card, 2m)));
            Assert.SequenceEqual(new[] { "AfterApplied" }, log.Where(l => l != "RecalculateValues"));
            Assert.True(ReferenceEquals(first, CardCmd.Afflict(cs, new RecordingAffliction(log, stackable: true), card, 3m)));
            Assert.Equal(5, first.Amount, "a same-type affliction stacks its amount");
            Assert.Equal(2, cs.History.CardAfflictedEntries.Count, "every successful afflict is recorded");
            Assert.Null(CardCmd.Afflict(cs, new RecordingAffliction(log, only: CardType.Skill), MakeIn(cs, PileType.Hand, StrikeIronclad.CardId), 1m), "CanAfflict false returns null");
            var afflicted = CardCmd.AfflictAndPreview(cs, new[] { MakeIn(cs, PileType.Hand, DefendIronclad.CardId), MakeIn(cs, PileType.Hand, Bash.CardId) }, Hexed.AfflictionId, 1m);
            Assert.Equal(2, afflicted.Count);
            Assert.False(ReferenceEquals(afflicted[0], afflicted[1]), "each card gets its own affliction model");
        });
    }

    public static void Test_CardKernel_PickRandomTargetsShufflesOnCombatCardGeneration()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var log = new List<string>();
        var cards = new List<CardInstance>();
        for (int i = 0; i < 5; i++)
        {
            cards.Add(MakeIn(cs, PileType.Hand, i % 2 == 0 ? StrikeIronclad.CardId : DefendIronclad.CardId));
        }
        var picker = new RecordingAffliction(log, only: CardType.Attack);
        var stream = cs.Rng.CombatCardGeneration;
        var reference = new List<CardInstance>(cards.Where(c => c.Type == CardType.Attack)).UnstableShuffle(stream.DeepClone());
        int before = stream.Counter;
        var picked = picker.PickRandomTargets(cs, cs.Rng, cards, 2);
        Assert.SequenceEqual(reference.Take(2), picked, "Where(CanAfflict), UnstableShuffle, keep the first count");
        Assert.Equal(before + 2, stream.Counter, "three eligible cards cost two draws");
        Assert.Equal(3, picker.PickRandomTargets(cs, cs.Rng, cards, 9).Count, "a count above the eligible cards keeps them all");
        Assert.Equal(0, picker.PickRandomTargets(cs, cs.Rng, cards, 0).Count);
        Assert.Equal(0, picker.PickRandomTargets(cs, cs.Rng, Array.Empty<CardInstance>(), 3).Count);
    }

    public static void Test_CardKernel_HexedClearsItselfUnlessTheOwnerHasHexPower()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var other = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        var hexed = new Hexed();
        card.AfflictInternal(hexed, 1m);
        Assert.Equal("HEXED", hexed.Id);
        Assert.Equal(DeterministicHash.Slugify(nameof(Hexed)), hexed.Id);
        hexed.AfterCardEnteredCombat(cs, other);
        Assert.True(ReferenceEquals(hexed, card.Affliction), "another card entering combat changes nothing");
        var hex = Give(cs, new IdPower(Hexed.HexPowerId));
        hexed.AfterCardEnteredCombat(cs, card);
        Assert.True(ReferenceEquals(hexed, card.Affliction), "the owner's HexPower keeps the affliction");
        hex.RemoveInternal();
        hexed.AfterCardEnteredCombat(cs, card);
        Assert.Null(card.Affliction, "without HexPower Hexed clears itself");
        Assert.True(AfflictionRegistry.Models.Contains(Hexed.AfflictionId));
    }

    public static void Test_CardKernel_CardListenersAreTheCardThenItsAfflictionThenItsEnchantment()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Hand, new ListenerCardDef());
        var log = new List<string>();
        var enchantment = new RecordingEnchantment(log);
        CardCmd.Enchant(enchantment, card, 1m);
        var affliction = new RecordingAffliction(log);
        card.AfflictInternal(affliction, 1m);
        var listeners = new List<AbstractModel>();
        card.AppendHookListeners(listeners);
        Assert.Equal(3, listeners.Count);
        Assert.True(ReferenceEquals(card.Listener, listeners[0]), "the card's own listener first");
        Assert.True(ReferenceEquals(affliction, listeners[1]), "then its affliction");
        Assert.True(ReferenceEquals(enchantment, listeners[2]), "then its enchantment (Combat/CombatState.cs:903-911)");
        Assert.True(card.IsLiveHookListener && affliction.IsLiveHookListener && enchantment.IsLiveHookListener);
        cs.Player.DeactivateHooks();
        Assert.False(card.IsLiveHookListener || affliction.IsLiveHookListener || enchantment.IsLiveHookListener, "an inactive owner silences the card's listeners");
        cs.Player.ActivateHooks();
        card.HasBeenRemovedFromState = true;
        Assert.False(card.IsLiveHookListener || affliction.IsLiveHookListener || enchantment.IsLiveHookListener, "a removed card silences its listeners");
        Assert.False(new RecordingEnchantment(log).IsLiveHookListener, "an unapplied enchantment is never live");
        Assert.False(CardInstance.Create(cs, StrikeIronclad.CardId).IsLiveHookListener, "an ownerless card is never live");
    }

    public static void Test_CardKernel_TheHookDispatchVisitsCardListenersInOrder()
    {
        W1Pending.Run(nameof(Test_CardKernel_TheHookDispatchVisitsCardListenersInOrder), HooksArea, () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            var log = new List<string>();
            var first = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
            var second = MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
            first.AfflictInternal(new RecordingAffliction(log, tag: "A1"), 1m);
            CardCmd.Enchant(new RecordingEnchantment(log, tag: "E1"), first, 1m);
            CardCmd.Enchant(new RecordingEnchantment(log, tag: "E2"), second, 1m);
            log.Clear();
            Hook.AfterCardEnteredCombat(cs, first);
            if (log.Count == 0)
            {
                throw new NotImplementedException("Hook dispatch does not visit card afflictions and enchantments yet (CONTRACTS Part B section B3, hooks owner)");
            }
            Assert.SequenceEqual(new[] { "A1:AfterCardEnteredCombat", "E1:AfterCardEnteredCombat", "E2:AfterCardEnteredCombat" }, log);
        });
    }

    public static void Test_CardKernel_InCombatKeywordsAddTheHookModifiersOnlyInCombatPiles()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var inHand = MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var loose = Make(cs, StrikeIronclad.CardId);
        Give(cs, new AddKeywordPower(CardKeyword.Ethereal));
        Assert.True(inHand.HasKeywordInCombat(cs, CardKeyword.Ethereal), "a card in a combat pile sees the hook's keyword");
        Assert.False(inHand.HasKeyword(CardKeyword.Ethereal), "the local keyword set is untouched");
        Assert.False(loose.HasKeywordInCombat(cs, CardKeyword.Ethereal), "a card outside the combat piles has local keywords only");
        Assert.False(inHand.HasKeywordInCombat(null, CardKeyword.Ethereal));
        Assert.SequenceEqual(new[] { CardKeyword.Ethereal }, inHand.GetKeywordsWithSources(cs, KeywordSources.Global));
        Assert.Equal(0, inHand.GetKeywordsWithSources(cs, KeywordSources.Local).Count);
        Assert.Equal(0, inHand.GetKeywordsWithSources(cs, KeywordSources.None).Count);
        inHand.AddKeyword(CardKeyword.Retain);
        Assert.SequenceEqual(new[] { CardKeyword.Ethereal, CardKeyword.Retain }, inHand.Keywords(cs));
        Assert.True(inHand.ShouldRetainThisTurnIn(cs));

        var unplayable = Give(cs, new AddKeywordPower(CardKeyword.Unplayable, "CARD_KERNEL_TEST_UNPLAYABLE_POWER"));
        Assert.False(inHand.CanPlay(cs, out var reason));
        Assert.Equal(UnplayableReason.HasUnplayableKeyword, reason, "CanPlay reads the in-combat keyword set");
        unplayable.RemoveInternal();
        Assert.True(inHand.CanPlay(cs));

        var exhaust = Give(cs, new AddKeywordPower(CardKeyword.Exhaust, "CARD_KERNEL_TEST_EXHAUST_POWER"));
        cs.Player.PlayPile.AddInternal(loose);
        Assert.Equal(PileType.Exhaust, loose.GetResultLocationForCardPlay(cs).PileType, "the result pile reads the in-combat Exhaust keyword");
        exhaust.RemoveInternal();
        Assert.Equal(PileType.Discard, loose.GetResultLocationForCardPlay(cs).PileType);

        var notInCombat = new PlayerState(5, StartingDecks.IroncladCharacterId, 50, 50, 3);
        var foreign = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), 900);
        foreign.AttachTo(notInCombat);
        Assert.False(foreign.CanPlay(cs, out var foreignReason, out var preventer), "a card whose owner is not in the combat cannot be played");
        Assert.Equal(UnplayableReason.None, foreignReason);
        Assert.Null(preventer);
    }

    public static void Test_CardKernel_CardCmdKeywordsAndSingleTurnFlags()
    {
        var card = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), 1);
        CardCmd.ApplyKeyword(card, CardKeyword.Exhaust, CardKeyword.Retain);
        Assert.True(card.HasKeyword(CardKeyword.Exhaust) && card.HasKeyword(CardKeyword.Retain));
        CardCmd.RemoveKeyword(card, CardKeyword.Exhaust);
        Assert.False(card.HasKeyword(CardKeyword.Exhaust));
        CardCmd.ApplySingleTurnSly(card);
        CardCmd.ApplySingleTurnRetain(card);
        Assert.True(card.IsSlyThisTurn && card.HasSingleTurnRetain);
        card.EndOfTurnCleanup();
        Assert.False(card.HasSingleTurnSly || card.HasSingleTurnRetain);
        Assert.True(card.ShouldRetainThisTurn, "the Retain keyword survives the turn end");

        var set = new CardKeywordSet();
        Assert.True(set.Add(CardKeyword.Sly));
        Assert.False(set.Add(CardKeyword.Sly));
        set.UnionWith(new[] { CardKeyword.Innate, CardKeyword.Eternal });
        Assert.Equal(3, set.Count);
        Assert.True(set.IsSupersetOf(new[] { CardKeyword.Innate }));
        Assert.True(set.Remove(CardKeyword.Innate));
        Assert.SequenceEqual(new[] { CardKeyword.Sly, CardKeyword.Eternal }, set);
        Assert.True(set.SetEquals(new[] { CardKeyword.Eternal, CardKeyword.Sly }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardKeywordSet.BitOf((CardKeyword)40));
    }

    public static void Test_CardKernel_ResourceChecksUseTheCombatStateGuard()
    {
        var cs = NewCombat(energy: 1);
        AddMonster(cs);
        var bash = Make(cs, Bash.CardId);
        cs.Player.Stars = 5;
        Give(cs, new PayWithStarsPower());
        Assert.False(bash.HasEnoughResourcesFor(cs, out var outside), "outside the combat piles excess energy is not paid with stars");
        Assert.Equal(UnplayableReason.EnergyCostTooHigh, outside);
        cs.Player.Hand.AddInternal(bash);
        Assert.True(bash.HasEnoughResourcesFor(cs, out _), "in hand two stars pay the missing energy");

        var strike = Make(cs, StrikeIronclad.CardId);
        Give(cs, new FreeEnergyPower());
        Assert.Equal(1, strike.EnergyCost.GetWithModifiers(cs, strike, CostModifiers.All), "the global energy hook skips a card outside the combat piles");
        cs.Player.Hand.AddInternal(strike);
        Assert.Equal(0, strike.EnergyCost.GetWithModifiers(cs, strike, CostModifiers.All));
        Assert.Equal(0, strike.EnergyCost.GetAmountToSpend(cs, strike));
        Assert.Equal(1, strike.EnergyCost.GetWithModifiers(CostModifiers.All), "the context-free overload is the out-of-combat path");
    }

    public static void Test_CardKernel_StarCostsTemporaryCostsAndTheXStarCapture()
    {
        var cs = NewCombat();
        AddMonster(cs);
        cs.Player.Stars = 4;
        var starX = MakeIn(cs, PileType.Hand, new StarXCard());
        Assert.Equal(4, starX.GetStarCostWithModifiers(cs), "an X star cost is the owner's current stars");
        starX.LastStarsSpent = 3;
        Assert.Equal(3, starX.ResolveStarXValue(cs), "the X value is the captured spend");
        Assert.Throws<InvalidOperationException>(() => MakeIn(cs, PileType.Hand, StrikeIronclad.CardId).ResolveStarXValue(cs));

        var star = MakeIn(cs, PileType.Hand, new StarCard());
        Assert.Equal(StarCard.Stars, star.BaseStarCost);
        star.SetStarCostThisCombat(2);
        star.SetStarCostUntilPlayed(1);
        star.SetStarCostThisTurn(0);
        Assert.Equal(0, star.CurrentStarCost, "the last temporary cost wins");
        Assert.Equal(2, star.GetStarCostThisCombat());
        star.EndOfTurnCleanup();
        Assert.Equal(1, star.CurrentStarCost);
        star.AfterCardPlayedCleanup();
        Assert.Equal(2, star.CurrentStarCost);
        star.UpgradeStarCostBy(-2);
        Assert.Equal(StarCard.Stars - 2, star.BaseStarCost);
        Assert.True(star.WasStarCostJustUpgraded);
        Assert.Equal(StarCard.Stars - 2, star.CurrentStarCost, "temporary costs above the new base are removed");
        star.FinalizeUpgradeInternal();
        Assert.False(star.WasStarCostJustUpgraded);

        cs.Player.Energy = 2;
        var energyX = MakeIn(cs, PileType.Hand, new EnergyXCard());
        energyX.CaptureXValuesForAutoPlay(cs, skipXCapture: false);
        Assert.Equal(2, energyX.EnergyCost.CapturedXValue, "auto-play captures the owner's energy for an X cost");
        Assert.Equal(0, energyX.LastStarsSpent);
        starX.CaptureXValuesForAutoPlay(cs, skipXCapture: false);
        Assert.Equal(4, starX.LastStarsSpent, "auto-play captures the owner's stars for an X star cost");
        star.CaptureXValuesForAutoPlay(cs, skipXCapture: false);
        Assert.Equal(StarCard.Stars - 2, star.LastStarsSpent, "a fixed star cost is captured as max(0, cost with modifiers)");
        cs.Player.Energy = 3;
        energyX.CaptureXValuesForAutoPlay(cs, skipXCapture: true);
        Assert.Equal(2, energyX.EnergyCost.CapturedXValue, "skipXCapture keeps the captured values");
    }

    public static void Test_CardKernel_CardsCompareByIdThenUpgradeLevelAndFindTheirDeckVersion()
    {
        var cs = NewCombat();
        var bash = Make(cs, Bash.CardId);
        var strike = Make(cs, StrikeIronclad.CardId);
        var strikePlus = Make(cs, StrikeIronclad.CardId);
        strikePlus.Upgrade();
        var sorted = new List<CardInstance> { strikePlus, strike, bash };
        sorted.Sort();
        Assert.SequenceEqual(new[] { bash, strike, strikePlus }, sorted, "ordinal id, then CurrentUpgradeLevel (Models/CardModel.cs:3649-3671)");
        Assert.Equal(0, strike.CompareTo(strike));
        Assert.Equal(1, strike.CompareTo(null));

        var deckCard = MakeDeck(cs, Bash.CardId);
        var copy = deckCard.CloneForCombat(cs.AllocateCardInstanceId());
        Assert.True(ReferenceEquals(deckCard, copy.FindDeckVersion()));
        Assert.Null(deckCard.FindDeckVersion(), "a deck card has no deck version");
        cs.Player.Deck.Remove(deckCard);
        Assert.Null(copy.FindDeckVersion(), "the link resolves through the owner's deck");
    }

    public static void Test_CardKernel_GetDistinctForCombatShufflesOnceAndCreatesLazily()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var defs = new[] { Armaments.CardId, Headbutt.CardId, Bash.CardId, SeekerStrike.CardId, AscendersBane.CardId, Discovery.CardId, Armaments.CardId, Acrobatics.CardId, Seance.CardId }
            .Select(CardRegistry.Get).ToList();
        var filtered = new[] { Armaments.CardId, Headbutt.CardId, SeekerStrike.CardId, Discovery.CardId, Acrobatics.CardId, Seance.CardId };
        Assert.SequenceEqual(filtered, CardFactory.FilterForCombat(defs).Select(d => d.Id), "FilterForCombat drops Basic, non-generatable and duplicate cards, keeping order");
        var stream = cs.Rng.CombatCardGeneration;
        var reference = filtered.ToList().UnstableShuffle(stream.DeepClone()).Take(3).ToList();
        int before = stream.Counter;
        int nextId = cs.NextCardInstanceId;
        var lazy = CardFactory.GetDistinctForCombat(cs, cs.Player, defs, 3, stream);
        Assert.Equal(before + filtered.Length - 1, stream.Counter, "TakeRandom shuffles the whole option list at the call");
        Assert.Equal(nextId, cs.NextCardInstanceId, "the cards are created when the result is enumerated");
        var cards = lazy.ToList();
        Assert.SequenceEqual(reference, cards.Select(c => c.Id));
        Assert.SequenceEqual(new[] { nextId, nextId + 1, nextId + 2 }, cards.Select(c => c.InstanceId));
        Assert.True(cards.All(c => ReferenceEquals(cs.Player, c.Owner)));
        Assert.Equal(0, cs.Run.Fidelity.Count, "a fully ported option list is exact");
    }

    public static void Test_CardKernel_GetForCombatDrawsOneItemPerCard()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var defs = new[] { Armaments.CardId, Headbutt.CardId, Bash.CardId, Discovery.CardId }.Select(CardRegistry.Get).ToList();
        var options = new[] { Armaments.CardId, Headbutt.CardId, Discovery.CardId };
        var stream = cs.Rng.CombatCardGeneration;
        var reference = stream.DeepClone();
        var expected = Enumerable.Range(0, 5).Select(_ => Assert.NotNull(reference.NextItem((IReadOnlyList<string>)options))).ToList();
        int before = stream.Counter;
        var cards = CardFactory.GetForCombat(cs, cs.Player, defs, 5, stream);
        Assert.SequenceEqual(expected, cards.Select(c => c.Id), "NextItem with replacement, one draw per card");
        Assert.Equal(before + 5, stream.Counter);
        Assert.Equal(0, CardFactory.GetForCombat(cs, cs.Player, defs, 0, stream).Count);
    }

    public static void Test_CardKernel_FactoryDropsUnportedPoolCardsWithAFidelityFlag()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var pool = CardPools.Ironclad.GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run));
        var eligible = CardFactory.FilterForCombat(pool).ToList();
        var ported = eligible.Where(d => d is not UnportedCardDef).Select(d => d.Id).ToList();
        Assert.True(ported.Contains(Armaments.CardId) && ported.Contains(Headbutt.CardId), "the ported Ironclad options include the W1 pilots");
        var stream = cs.Rng.CombatCardGeneration;
        var reference = ported.ToList().UnstableShuffle(stream.DeepClone());
        int before = stream.Counter;
        var cards = CardFactory.GetDistinctForCombat(cs, cs.Player, pool, 3, stream).ToList();
        Assert.SequenceEqual(reference.Take(3), cards.Select(c => c.Id), "the draw runs over the ported options only");
        Assert.Equal(before + ported.Count - 1, stream.Counter);
        var flag = cs.Run.Fidelity.Entries.Single();
        Assert.Equal(FidelityFlag.ApproximatedRule, flag.Flag);
        Assert.Equal($"CardFactory.GetDistinctForCombat: {eligible.Count - ported.Count} of {eligible.Count} options are not ported; the draw runs over the ported ones", flag.Site);
        Assert.True(pool.Any(d => d.Id == "TANK") == false, "single-player pools drop the MultiplayerOnly cards");
        Assert.True(CardPools.Ironclad.AllCards.Any(d => d.Id == "TANK"));
    }

    public static void Test_CardKernel_DiscoveryOffersGeneratedCardsAndAddsTheChoiceFreeThisTurn()
    {
        W1Pending.Run(nameof(Test_CardKernel_DiscoveryOffersGeneratedCardsAndAddsTheChoiceFreeThisTurn), ChoiceArea, () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            cs.Choice.Answerer = new PickFirstAnswerer();
            var discovery = MakeIn(cs, PileType.Play, Discovery.CardId);
            var stream = cs.Rng.CombatCardGeneration;
            var reference = new List<string> { Armaments.CardId, Headbutt.CardId }.UnstableShuffle(stream.DeepClone());
            discovery.OnPlay(cs, new CardPlay(cs, discovery, null));
            var chosen = cs.Player.Hand.Cards.Single();
            Assert.Equal(reference[0], chosen.Id, "the first option of the generation order");
            Assert.True(ReferenceEquals(cs.Player, chosen.Owner));
            Assert.Equal(0, chosen.EnergyCost.GetAmountToSpend(cs, chosen), "the chosen card is free this turn");
        });
    }

    public static void Test_CardKernel_HeadbuttPutsTheOnlyDiscardCardOnTopOfTheDrawPile()
    {
        W1Pending.Run(nameof(Test_CardKernel_HeadbuttPutsTheOnlyDiscardCardOnTopOfTheDrawPile), ChoiceArea, () =>
        {
            var cs = NewCombat();
            var monster = AddMonster(cs);
            var headbutt = MakeIn(cs, PileType.Play, Headbutt.CardId);
            var draw = MakeIn(cs, PileType.Draw, StrikeIronclad.CardId);
            var discarded = MakeIn(cs, PileType.Discard, DefendIronclad.CardId);
            headbutt.OnPlay(cs, new CardPlay(cs, headbutt, monster));
            Assert.Equal(40 - (int)Headbutt.BaseDamage, monster.CurrentHp);
            Assert.SequenceEqual(new[] { discarded, draw }, cs.Player.DrawPile.Cards, "the selected card goes on top of the draw pile");
            Assert.Equal(0, cs.Player.DiscardPile.Count);
        });
    }

    public static void Test_CardKernel_SeekerStrikeDrawsTheOnlyOptionIntoTheHand()
    {
        W1Pending.Run(nameof(Test_CardKernel_SeekerStrikeDrawsTheOnlyOptionIntoTheHand), ChoiceArea, () =>
        {
            var cs = NewCombat();
            var monster = AddMonster(cs);
            var seeker = MakeIn(cs, PileType.Play, SeekerStrike.CardId);
            var only = MakeIn(cs, PileType.Draw, DefendIronclad.CardId);
            int before = cs.Rng.CombatCardSelection.Counter;
            seeker.OnPlay(cs, new CardPlay(cs, seeker, monster));
            Assert.Equal(40 - (int)SeekerStrike.BaseDamage, monster.CurrentHp);
            Assert.Equal(before, cs.Rng.CombatCardSelection.Counter, "a one-card draw pile shuffles without a draw");
            Assert.SequenceEqual(new[] { only }, cs.Player.Hand.Cards);
        });
    }

    public static void Test_CardKernel_AcrobaticsDrawsThenDiscardsTheOnlyHandCard()
    {
        W1Pending.Run(nameof(Test_CardKernel_AcrobaticsDrawsThenDiscardsTheOnlyHandCard), ChoiceArea, () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            var acrobatics = MakeIn(cs, PileType.Play, Acrobatics.CardId);
            var only = MakeIn(cs, PileType.Draw, StrikeIronclad.CardId);
            acrobatics.OnPlay(cs, new CardPlay(cs, acrobatics, null));
            Assert.Equal(0, cs.Player.Hand.Count);
            Assert.SequenceEqual(new[] { only }, cs.Player.DiscardPile.Cards, "the drawn card is the only discard option");
        });
    }

    public static void Test_CardKernel_SeanceTransformsTheOnlyDrawCardIntoSoul()
    {
        W1Pending.Run(nameof(Test_CardKernel_SeanceTransformsTheOnlyDrawCardIntoSoul), ChoiceArea, () =>
        {
            var cs = NewCombat();
            AddMonster(cs);
            var seance = MakeIn(cs, PileType.Play, Seance.CardId);
            var only = MakeIn(cs, PileType.Draw, StrikeIronclad.CardId);
            seance.OnPlay(cs, new CardPlay(cs, seance, null));
            Assert.Equal(Soul.CardId, cs.Player.DrawPile.Cards.Single().Id);
            Assert.True(only.HasBeenRemovedFromState);
        });
    }

    public static void Test_CardKernel_PoolsFollowGenerateAllCardsOrder()
    {
        var expectedSizes = new (CardPool Pool, int Size, string First, string Last)[]
        {
            (CardPools.Ironclad, 90, "AGGRESSION", "WHIRLWIND"),
            (CardPools.Silent, 91, "ABRASIVE", "WRAITH_FORM"),
            (CardPools.Regent, 91, "ALIGNMENT", "WROUGHT_IN_WAR"),
            (CardPools.Necrobinder, 91, "AFTERLIFE", "WISP"),
            (CardPools.Defect, 91, "ADAPTIVE_STRIKE", "ZAP"),
            (CardPools.Colorless, 65, "ALCHEMIZE", "VOLLEY"),
            (CardPools.Curse, 18, "ASCENDERS_BANE", "WRITHE"),
            (CardPools.Deprecated, 1, "DEPRECATED_CARD", "DEPRECATED_CARD"),
            (CardPools.Event, 28, "ABUNDANCE", "WHISTLE"),
            (CardPools.Quest, 4, "BYRDONIS_EGG", "SPOILS_MAP"),
            (CardPools.Status, 12, "BECKON", "WOUND"),
            (CardPools.Token, 14, "DISINTEGRATION", "WASTE_AWAY"),
        };
        foreach (var (pool, size, first, last) in expectedSizes)
        {
            Assert.Equal(size, pool.AllCards.Count, pool.Id + " AllCards size");
            Assert.Equal(first, pool.AllCardIds[0], pool.Id + " first card");
            Assert.Equal(last, pool.AllCardIds[^1], pool.Id + " last card");
            Assert.Equal(size, pool.AllCardIds.Distinct().Count(), pool.Id + " has distinct ids");
            for (int i = 0; i < size; i++)
            {
                Assert.Equal(pool.AllCardIds[i], pool.AllCards[i].Id);
            }
        }
        Assert.SequenceEqual(new[] { "AGGRESSION", "ANGER", "ARMAMENTS", "ASHEN_STRIKE", "BARRICADE", "BASH" }, CardPools.Ironclad.AllCardIds.Take(6));
        Assert.True(ReferenceEquals(CardRegistry.Get(Armaments.CardId), CardPools.Ironclad.AllCards[2]), "registered cards resolve to their registered def");
        Assert.True(CardPools.Ironclad.AllCards[0] is UnportedCardDef, "unported cards resolve to catalog placeholders");
        Assert.SequenceEqual(new[] { "IRONCLAD", "SILENT", "REGENT", "NECROBINDER", "DEFECT" }, CardPools.AllCharacterIds);
        Assert.SequenceEqual(new[] { "COLORLESS_CARD_POOL", "CURSE_CARD_POOL", "DEPRECATED_CARD_POOL", "EVENT_CARD_POOL", "QUEST_CARD_POOL", "STATUS_CARD_POOL", "TOKEN_CARD_POOL" }, CardPools.AllSharedCardPools.Select(p => p.Id));
        Assert.Equal(12, CardPools.AllCardPools.Count);
        Assert.Equal(596, CardCatalog.Count, "every card of every v0.111.0 pool has a catalog entry");
    }

    public static void Test_CardKernel_UnlockedCardCountsPerProfileAndConstraint()
    {
        var characterPools = new[] { CardPools.Ironclad, CardPools.Silent, CardPools.Regent, CardPools.Necrobinder, CardPools.Defect };
        var allCardsSizes = new[] { 90, 91, 91, 91, 91 };
        var singleplayer = new[] { 85, 86, 86, 86, 86 };
        for (int i = 0; i < characterPools.Length; i++)
        {
            var pool = characterPools[i];
            var unlocked = pool.GetUnlockedCards(UnlockProfile.All, CardMultiplayerConstraint.SingleplayerOnly);
            Assert.Equal(singleplayer[i], unlocked.Count, pool.Id + " unlocked single-player count (B12 A7)");
            Assert.Equal(allCardsSizes[i], pool.GetUnlockedCards(UnlockProfile.All, CardMultiplayerConstraint.MultiplayerOnly).Count, pool.Id + " no SingleplayerOnly card exists");
            Assert.Equal(allCardsSizes[i], pool.GetUnlockedCards(UnlockProfile.All, CardMultiplayerConstraint.None).Count);
            Assert.Equal(5, pool.AllCards.Count(c => c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly), pool.Id + " MultiplayerOnly cards");
            Assert.SequenceEqual(pool.AllCards.Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly), unlocked, pool.Id + " keeps pool order");
            Assert.Equal(allCardsSizes[i] - 9 - 5, pool.GetUnlockedCards(UnlockProfile.None, CardMultiplayerConstraint.SingleplayerOnly).Count, pool.Id + " with no epoch revealed");
        }
        Assert.Equal(53, CardPools.Colorless.GetUnlockedCards(UnlockProfile.All, CardMultiplayerConstraint.SingleplayerOnly).Count, "colorless unlocked single-player count (B12 A7)");
        Assert.Equal(65, CardPools.Colorless.AllCards.Count);
        Assert.Equal(12, CardPools.Colorless.AllCards.Count(c => c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly));
        Assert.Equal(65 - 15 - 12, CardPools.Colorless.GetUnlockedCards(UnlockProfile.None, CardMultiplayerConstraint.SingleplayerOnly).Count);
        Assert.Equal(37, CardPools.AllCardPools.Sum(p => p.AllCards.Count(c => c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)), "37 MultiplayerOnly cards");
        Assert.Equal(18, CardPools.Curse.GetUnlockedCards(UnlockProfile.None, CardMultiplayerConstraint.SingleplayerOnly).Count, "shared pools without FilterThroughEpochs are not gated");

        var allButIronclad2 = new UnlockProfile(UnlockProfile.AllEpochIds.Where(e => e != "IRONCLAD2_EPOCH"), UnlockProfile.AllEncounterIds, 5);
        var gated = CardPools.Ironclad.GetUnlockedCards(allButIronclad2, CardMultiplayerConstraint.SingleplayerOnly).Select(c => c.Id).ToList();
        Assert.Equal(82, gated.Count);
        foreach (var id in new[] { "MOLTEN_FIST", "CRUELTY", "DOMINATE" })
        {
            Assert.False(gated.Contains(id), id + " is gated by IRONCLAD2_EPOCH");
        }
        foreach (var id in new[] { "CINDER", "PACTS_END", "DRUM_OF_BATTLE", "BLOOD_WALL", "TEAR_ASUNDER", "INFERNO" })
        {
            Assert.True(gated.Contains(id), id);
        }
        foreach (var gate in CardPools.AllCardPools.SelectMany(p => p.EpochGates.Select(g => (Pool: p, Gate: g))))
        {
            Assert.True(UnlockProfile.IsValidEpoch(gate.Gate.EpochId), gate.Gate.EpochId);
            Assert.Equal(3, gate.Gate.CardIds.Count);
            foreach (var id in gate.Gate.CardIds)
            {
                Assert.True(gate.Pool.Contains(id), $"{id} gated by {gate.Gate.EpochId} is in {gate.Pool.Id}");
            }
        }
    }

    public static void Test_CardKernel_UnlockProfileCharactersAndCardPools()
    {
        Assert.SequenceEqual(new[] { "IRONCLAD", "SILENT", "REGENT", "NECROBINDER", "DEFECT" }, UnlockProfile.All.Characters);
        Assert.SequenceEqual(new[] { "IRONCLAD" }, UnlockProfile.None.Characters);
        var silent = new UnlockProfile(new[] { UnlockProfile.Silent1EpochId, UnlockProfile.Defect1EpochId }, Array.Empty<string>(), 1);
        Assert.SequenceEqual(new[] { "IRONCLAD", "SILENT", "DEFECT" }, silent.Characters);
        Assert.SequenceEqual(new[] { CardPools.Ironclad, CardPools.Silent, CardPools.Defect }, silent.CharacterCardPools);
        Assert.SequenceEqual(new[] { CardPools.Ironclad, CardPools.Silent, CardPools.Defect }.Concat(CardPools.AllSharedCardPools), silent.CardPools);
        Assert.Equal(12, UnlockProfile.All.CardPools.Count);
        Assert.True(ReferenceEquals(CardPools.Ironclad, CardPools.ForCharacter(StartingDecks.IroncladCharacterId)));
        Assert.Throws<KeyNotFoundException>(() => CardPools.ForCharacter("DEPRIVED"));
        Assert.True(ReferenceEquals(CardPools.Ironclad, CardRegistry.Get(Armaments.CardId).Pool));
        Assert.True(ReferenceEquals(CardPools.Colorless, CardRegistry.Get(Discovery.CardId).Pool));
        Assert.True(ReferenceEquals(CardPools.Token, CardRegistry.Get(Soul.CardId).Pool));
        Assert.True(ReferenceEquals(CardPools.Curse, CardRegistry.Get(AscendersBane.CardId).Pool));
        Assert.Throws<InvalidProgramException>(() => _ = new CalcCard().Pool, "a card in no pool throws");
        Assert.Equal(CardMultiplayerConstraint.SingleplayerOnly, CardPools.MultiplayerConstraintFor(NewCombat().Run));
    }

    public static void Test_CardKernel_RegisteredDefsAgreeWithTheCatalog()
    {
        int checkedDefs = 0;
        foreach (var id in CardRegistry.AllIds)
        {
            if (!CardCatalog.TryGet(id, out var entry))
            {
                continue;
            }
            var def = CardRegistry.Get(id);
            Assert.Equal(entry.Type, def.Type, id);
            Assert.Equal(entry.Rarity, def.Rarity, id);
            Assert.Equal(entry.TargetType, def.TargetType, id);
            Assert.Equal(entry.CanonicalCost, def.CanonicalCost, id);
            Assert.Equal(entry.CostsX, def.CostsX, id);
            Assert.Equal(entry.MultiplayerConstraint, def.MultiplayerConstraint, id);
            Assert.Equal(entry.CanBeGeneratedInCombat, def.CanBeGeneratedInCombat, id);
            Assert.Equal(entry.CanBeGeneratedByModifiers, def.CanBeGeneratedByModifiers, id);
            Assert.True(ReferenceEquals(def, CardCatalog.Resolve(id)), id);
            checkedDefs++;
        }
        Assert.Equal(CardRegistry.AllIds.Count, checkedDefs, "every registered card is in a v0.111.0 pool");
        var placeholder = CardCatalog.Resolve("AGGRESSION");
        Assert.True(placeholder is UnportedCardDef);
        Assert.False(CardCatalog.IsPorted("AGGRESSION"));
        Assert.Throws<NotImplementedException>(() => _ = new CardInstance(placeholder, 1), "a placeholder never becomes a card instance");
        Assert.Throws<NotImplementedException>(() => _ = placeholder.CanonicalKeywords);
        Assert.Throws<KeyNotFoundException>(() => CardCatalog.Get("NOT_A_CARD"));
        Assert.Equal(CardType.None, CardCatalog.Get("MAD_SCIENCE").Type, "MadScience's canonical Type is its unset TinkerTimeType");
    }

    public static void Test_CardKernel_DynamicVarSubclassesCarryTheGameNamesAndValues()
    {
        var vars = new DynamicVar[]
        {
            new BlockVar(5m, ValueProp.Move), new CardsVar(2), new DamageVar(6m, ValueProp.Unpowered), new EnergyVar(1), new ExtraDamageVar(3m).FromOsty(),
            new ForgeVar(4), new GoldVar(25), new HealVar(7m), new HpLossVar(2m), new MaxHpVar(3m), new OstyDamageVar(9m, ValueProp.Move),
            new RepeatVar(2), new StarsVar(1), new SummonVar(5m), new CalculationBaseVar(1m), new CalculationExtraVar(2m),
            new CalculatedDamageVar(ValueProp.Move), new CalculatedBlockVar(ValueProp.Move), new IfUpgradedVar(UpgradeDisplay.Upgraded),
            new IntVar("Times", 2m), new BoolVar("Flag", true), new StringVar("Name", "text"), new PowerVar<VulnerablePower>(2m)
        };
        Assert.SequenceEqual(new[]
        {
            "Block", "Cards", "Damage", "Energy", "ExtraDamage", "Forge", "Gold", "Heal", "HpLoss", "MaxHp", "OstyDamage",
            "Repeat", "Stars", "Summon", "CalculationBase", "CalculationExtra", "CalculatedDamage", "CalculatedBlock", "IfUpgraded",
            "Times", "Flag", "Name", "VulnerablePower"
        }, vars.Select(v => v.Name));
        var set = new DynamicVarSet(vars);
        Assert.Equal(ValueProp.Unpowered, set.Damage.Props);
        Assert.Equal(ValueProp.Move, set.Block.Props);
        Assert.Equal(2m, set.Cards.BaseValue);
        Assert.True(set.ExtraDamage.IsFromOsty);
        Assert.Equal(1, (int)UpgradeDisplay.Upgraded);
        Assert.Equal(1m, set["IfUpgraded"].BaseValue);
        Assert.True(((BoolVar)set["Flag"]).BoolVal);
        Assert.Equal(1m, set["Flag"].BaseValue);
        Assert.Equal("text", set["Name"].ToString());
        Assert.Equal(2m, set.Vulnerable.BaseValue);
        Assert.Equal(ValueProp.Move, set.CalculatedDamage.Props);
        Assert.Throws<InvalidCastException>(() => _ = new DynamicVarSet(new[] { new DynamicVar(DynamicVarSet.CardsName, 1m) }).Cards, "typed accessors cast like the game's");
        var flag = (BoolVar)set["Flag"];
        flag.BoolVal = false;
        Assert.Equal(0m, flag.BaseValue);
        Assert.Equal(DynamicVar.MaxBaseValue, new GoldVar(int.MaxValue).BaseValue, "BaseValue clamps at 999999999");
    }

    public static void Test_CardKernel_CalculatedVarMultipliesOnlyForInProgressCombatCards()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Hand, new CalcCard());
        MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        var damage = card.Vars.CalculatedDamage;
        var block = card.Vars.CalculatedBlock;
        Assert.Equal(CalcCard.Base + CalcCard.ExtraDamage * 2m, damage.Calculate(cs, card, null), "base + ExtraDamage x the hand count");
        Assert.Equal(CalcCard.Base + CalcCard.Extra * 2m, block.Calculate(cs, card, null), "base + CalculationExtra x the hand count");
        cs.IsInProgress = false;
        Assert.Equal(CalcCard.Base, damage.Calculate(cs, card, null), "the multiplier is 0 outside a combat in progress");
        cs.IsInProgress = true;
        cs.Player.Hand.RemoveInternal(card);
        Assert.Equal(CalcCard.Base, damage.Calculate(cs, card, null), "the multiplier is 0 for a card outside the combat piles");
        Assert.Throws<InvalidOperationException>(() => new CalculatedVar("X").Calculate(cs, card, null), "a calculated var needs a multiplier");
        Assert.Throws<InvalidOperationException>(() => new CalculatedVar("Y").WithMultiplier(CalcCard.HandCount).WithMultiplier(CalcCard.HandCount), "the multiplier is set once");
        var def = new CalcCard();
        Assert.Throws<InvalidOperationException>(() => new CalculatedVar("Z").WithMultiplier(def.InstanceMultiplier), "a multiplier bound to a model is rejected");
    }

    public static void Test_CardKernel_PilotDefinitionsMatchTheDecompile()
    {
        AssertDef<Armaments>("ARMAMENTS", 1, CardType.Skill, CardRarity.Common, TargetType.Self, Array.Empty<CardKeyword>(), Array.Empty<CardTag>(), ("Block", 5m, ValueProp.Move));
        AssertDef<Acrobatics>("ACROBATICS", 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self, Array.Empty<CardKeyword>(), Array.Empty<CardTag>(), ("Cards", 3m, ValueProp.None));
        AssertDef<Headbutt>("HEADBUTT", 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy, Array.Empty<CardKeyword>(), Array.Empty<CardTag>(), ("Damage", 9m, ValueProp.Move));
        AssertDef<Seance>("SEANCE", 1, CardType.Skill, CardRarity.Rare, TargetType.Self, new[] { CardKeyword.Ethereal }, Array.Empty<CardTag>(), ("Cards", 1m, ValueProp.None));
        AssertDef<Soul>("SOUL", 0, CardType.Skill, CardRarity.Token, TargetType.Self, new[] { CardKeyword.Exhaust }, Array.Empty<CardTag>(), ("Cards", 2m, ValueProp.None));
        AssertDef<Discovery>("DISCOVERY", 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self, new[] { CardKeyword.Exhaust }, Array.Empty<CardTag>());
        AssertDef<SeekerStrike>("SEEKER_STRIKE", 1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy, Array.Empty<CardKeyword>(), new[] { CardTag.Strike }, ("Damage", 9m, ValueProp.Move), ("Cards", 3m, ValueProp.None));
        AssertDef<SovereignBlade>("SOVEREIGN_BLADE", 2, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy, new[] { CardKeyword.Retain }, Array.Empty<CardTag>(),
            ("Damage", 10m, ValueProp.Move), ("CalculationBase", 0m, ValueProp.None), ("CalculationExtra", 1m, ValueProp.None), ("CalculatedBlock", 0m, ValueProp.Move), ("Repeat", 1m, ValueProp.None));
        Assert.True(CardRegistry.Get(Armaments.CardId).GainsBlock);

        var cs = NewCombat();
        var armaments = Make(cs, Armaments.CardId);
        armaments.Upgrade();
        Assert.Equal(5m, armaments.Vars.Block.BaseValue, "Armaments has no OnUpgrade value change");
        var acrobatics = Make(cs, Acrobatics.CardId);
        acrobatics.Upgrade();
        Assert.Equal(4m, acrobatics.Vars.Cards.BaseValue);
        var headbutt = Make(cs, Headbutt.CardId);
        headbutt.Upgrade();
        Assert.Equal(12m, headbutt.Vars.Damage.BaseValue);
        var seance = Make(cs, Seance.CardId);
        seance.Upgrade();
        Assert.Equal(0, seance.EnergyCost.Base);
        var soul = Make(cs, Soul.CardId);
        soul.Upgrade();
        Assert.Equal(3m, soul.Vars.Cards.BaseValue);
        var discovery = Make(cs, Discovery.CardId);
        discovery.Upgrade();
        Assert.False(discovery.HasKeyword(CardKeyword.Exhaust));
        var seeker = Make(cs, SeekerStrike.CardId);
        seeker.Upgrade();
        Assert.Equal(12m, seeker.Vars.Damage.BaseValue);
        Assert.Equal(3m, seeker.Vars.Cards.BaseValue);
        var blade = Make(cs, SovereignBlade.CardId);
        blade.Upgrade();
        Assert.Equal(1, blade.EnergyCost.Base);
        Assert.Equal(3, Soul.Create(cs, cs.Player, 3).Count(c => c.HasOwner && c.Id == Soul.CardId));
        Assert.True(EnchantmentRegistry.Models.Contains(Glam.EnchantmentId));
        Assert.Equal(DeterministicHash.Slugify(nameof(Glam)), Glam.EnchantmentId);
    }

    public static void Test_CardKernel_SovereignBladeTargetsBlockAndForgeState()
    {
        var cs = NewCombat();
        var a = AddMonster(cs);
        var b = AddMonster(cs);
        var blade = MakeIn(cs, PileType.Play, SovereignBlade.CardId);
        var def = (SovereignBlade)blade.Def;
        IForgeBladeDef forge = def;
        Assert.Equal(TargetType.AnyEnemy, blade.TargetType);
        Assert.False(blade.GainsBlock);
        def.SetRepeats(blade, 2m);
        forge.AddDamage(blade, 2m);
        Assert.Equal(12m, def.GetCurrentDamage(blade));
        Assert.Equal(2m, def.GetCurrentRepeats(blade));
        blade.OnPlay(cs, new CardPlay(cs, blade, a));
        Assert.Equal(40 - 24, a.CurrentHp, "two hits of the forged damage on the target");
        Assert.Equal(40, b.CurrentHp);
        Assert.Equal(0, cs.Player.Block);

        var parry = Give(cs, new IdPower(SovereignBlade.ParryPowerId), 3);
        var edge = Give(cs, new IdPower(SovereignBlade.SeekingEdgePowerId));
        Assert.Equal(TargetType.AllEnemies, blade.TargetType, "SeekingEdge makes the blade hit all enemies");
        Assert.True(blade.GainsBlock, "Parry makes the blade gain block");
        Assert.True(blade.IsValidTarget(cs, null));
        blade.OnPlay(cs, new CardPlay(cs, blade, null));
        Assert.Equal(0, a.CurrentHp);
        Assert.Equal(40 - 24, b.CurrentHp, "every enemy is hit");
        Assert.Equal(3, cs.Player.Block, "block = CalculationBase + CalculationExtra x Parry");
        edge.RemoveInternal();
        parry.RemoveInternal();

        Assert.False(forge.GetCreatedThroughForge(blade));
        forge.SetCreatedThroughForge(blade, true);
        ulong hash = StateHasher.Hash(blade);
        Assert.Equal(hash, StateHasher.Hash(blade.CloneFor(cs.Player)), "the blade state is cloned exactly");
        var copy = blade.CloneForCombat(cs.AllocateCardInstanceId());
        Assert.False(forge.GetCreatedThroughForge(copy), "AfterCloned resets CreatedThroughForge");
        Assert.True(forge.GetCreatedThroughForge(blade));
        Assert.Equal(12m, def.GetCurrentDamage(copy));
        forge.SetCreatedThroughForge(blade, false);
        Assert.NotEqual(hash, StateHasher.Hash(blade), "the blade state is in the canonical form");
        Assert.Throws<ArgumentException>(() => def.AddDamage(Make(cs, StrikeIronclad.CardId), 1m));
    }

    public static void Test_CardKernel_CardHashCoversEveryKernelMember()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = BuildLoadedCard(cs);
        var bytes = StateHasher.CanonicalBytes(card);
        ulong hash = StateHasher.Hash(card);
        Assert.SequenceEqual(bytes, StateHasher.CanonicalBytes(card.CloneFor(cs.Player)), "Hash(x) == Hash(Clone(x))");
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(cs.DeepClone()), "the combat clone carries the loaded card");

        var other = new PlayerState(7, StartingDecks.IroncladCharacterId, 50, 50, 3);
        var mutations = new (string Name, Func<CardInstance> Mutate)[]
        {
            ("instance id", () => card.CloneForCombat(card.InstanceId + 1).Also(c => c.DeckVersionId = card.DeckVersionId)),
            ("upgrade level", () => Clone(card).Also(c => c.UpgradeLevel = 1)),
            ("keywords", () => Clone(card).Also(c => c.AddKeyword(CardKeyword.Sly))),
            ("exhaust on next play", () => Clone(card).Also(c => c.ExhaustOnNextPlay = !c.ExhaustOnNextPlay)),
            ("single-turn retain", () => Clone(card).Also(c => c.HasSingleTurnRetain = !c.HasSingleTurnRetain)),
            ("single-turn sly", () => Clone(card).Also(c => c.HasSingleTurnSly = !c.HasSingleTurnSly)),
            ("base replay count", () => Clone(card).Also(c => c.BaseReplayCount++)),
            ("dupe flag", () => Clone(card).Also(c => c.IsDupe = !c.IsDupe)),
            ("current target", () => Clone(card).Also(c => c.CurrentTargetId = 3)),
            ("current play index", () => Clone(card).Also(c => c.CurrentPlayIndex++)),
            ("base star cost", () => Clone(card).Also(c => c.UpgradeStarCostBy(1))),
            ("last stars spent", () => Clone(card).Also(c => c.LastStarsSpent++)),
            ("temporary star costs", () => Clone(card).Also(c => c.SetStarCostUntilPlayed(0))),
            ("energy cost", () => Clone(card).Also(c => c.EnergyCost.AddThisTurn(1))),
            ("var value", () => Clone(card).Also(c => c.Vars.Damage.BaseValue += 1m)),
            ("var upgraded flag", () => Clone(card).Also(c => c.Vars.Damage.WasJustUpgraded = true)),
            ("owner", () => card.CloneFor(other)),
            ("no owner", () => card.CloneFor(null)),
            ("deck version", () => Clone(card).Also(c => c.DeckVersionId++)),
            ("removed from state", () => Clone(card).Also(c => c.HasBeenRemovedFromState = !c.HasBeenRemovedFromState)),
            ("enchantment amount", () => Clone(card).Also(c => c.Enchantment!.Amount++)),
            ("enchantment status", () => Clone(card).Also(c => c.Enchantment!.Status = EnchantmentStatus.Disabled)),
            ("enchantment var", () => Clone(card).Also(c => c.Enchantment!.DynamicVars[Glam.TimesKey].BaseValue = 4m)),
            ("enchantment state", () => Clone(card).Also(c => ((Glam)c.Enchantment!).AfterCardPlayed(cs, new CardPlay(cs, c, null)))),
            ("enchantment presence", () => Clone(card).Also(c => c.ClearEnchantmentInternal())),
            ("affliction amount", () => Clone(card).Also(c => c.Affliction!.Amount++)),
            ("affliction presence", () => Clone(card).Also(c => c.Affliction!.ClearInternal()).Also(c => c.Affliction = null)),
            ("listener payload", () => Clone(card).Also(c => ((CountingListener)c.Listener!).Count++)),
            ("listener type", () => Clone(card).Also(c => c.Listener = new OtherListener(c))),
            ("listener presence", () => Clone(card).Also(c => c.Listener = null)),
            ("extra state", () => Clone(card).Also(c => ((SovereignBladeStateProbe)c.Def).Touch(c))),
        };
        foreach (var (name, mutate) in mutations)
        {
            var changed = mutate();
            Assert.NotEqual(hash, StateHasher.Hash(changed), $"a change of {name} changes the hash");
            Assert.SequenceEqual(bytes, StateHasher.CanonicalBytes(card), $"changing a clone's {name} leaves the source unchanged");
        }
        var finalized = Clone(card).Also(c => c.UpgradeStarCostBy(1)).Also(c => c.FinalizeUpgradeInternal());
        var pending = Clone(card).Also(c => c.UpgradeStarCostBy(1));
        Assert.NotEqual(StateHasher.Hash(finalized), StateHasher.Hash(pending), "WasStarCostJustUpgraded is in the canonical form");
        Assert.SequenceEqual(bytes, StateHasher.CanonicalBytes(card));
    }

    private static CardInstance BuildLoadedCard(CombatState cs)
    {
        var card = new CardInstance(new SovereignBladeStateProbe(), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        cs.Player.Hand.AddInternal(card);
        CardCmd.Enchant(new Glam(), card, 1m);
        card.AfflictInternal(new RecordingAffliction(new List<string>()), 2m);
        card.DeckVersionId = 42;
        card.SetStarCostThisCombat(2);
        card.EnergyCost.SetThisCombat(0);
        card.BaseReplayCount = 1;
        card.LastStarsSpent = 1;
        return card;
    }

    private static CardInstance Clone(CardInstance card) => card.CloneFor(card.Owner);

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    private static void AssertDef<T>(string id, int cost, CardType type, CardRarity rarity, TargetType target, CardKeyword[] keywords, CardTag[] tags, params (string Name, decimal Value, ValueProp Props)[] vars) where T : CardDef
    {
        var def = CardRegistry.Get(id);
        Assert.True(def is T, id + " is registered");
        Assert.Equal(DeterministicHash.Slugify(typeof(T).Name), id);
        Assert.Equal(cost, def.CanonicalCost, id + " cost");
        Assert.Equal(type, def.Type, id + " type");
        Assert.Equal(rarity, def.Rarity, id + " rarity");
        Assert.Equal(target, def.TargetType, id + " target");
        Assert.SequenceEqual(keywords, def.CanonicalKeywords, id + " keywords");
        Assert.SequenceEqual(tags, def.CanonicalTags, id + " tags");
        var created = def.CreateVars();
        Assert.SequenceEqual(vars.Select(v => v.Name), created.All.Select(v => v.Name), id + " var names");
        Assert.SequenceEqual(vars.Select(v => v.Value), created.All.Select(v => v.BaseValue), id + " var values");
        Assert.SequenceEqual(vars.Select(v => v.Props), created.All.Select(v => v.Props), id + " var props");
        Assert.True(CardCatalog.Contains(id), id + " is in a pool");
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CARD_KERNEL_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CARD_KERNEL_TESTS_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private static CardInstance Make(CombatState cs, string id) => cs.CreateCard(CardRegistry.Get(id), cs.Player);

    private static CardInstance MakeIn(CombatState cs, PileType pile, string id) => MakeIn(cs, pile, CardRegistry.Get(id));

    private static CardInstance MakeIn(CombatState cs, PileType pile, CardDef def)
    {
        var card = cs.CreateCard(def, cs.Player);
        cs.Player.GetPile(pile).AddInternal(card);
        return card;
    }

    private static CardInstance MakeDeck(CombatState cs, string id)
    {
        var card = new CardInstance(CardRegistry.Get(id), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        cs.Player.Deck.Add(card);
        return card;
    }

    private static T Give<T>(CombatState cs, T power, int amount = 1) where T : PowerModel
    {
        power.ApplyInternal(cs.Player, amount);
        return power;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CARD_KERNEL_TESTS_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class CalcCard : CardDef
    {
        public const decimal Base = 3m;
        public const decimal BaseUpgrade = 4m;
        public const decimal Extra = 5m;
        public const decimal ExtraDamage = 2m;

        public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> HandCount = static (_, card, _) => card.Owner.Hand.Count;

        public CalcCard()
            : base("CARD_KERNEL_TEST_CALC", 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
        {
            new DamageVar(1m, ValueProp.Move),
            new CalculationBaseVar(Base),
            new CalculationExtraVar(Extra),
            new ExtraDamageVar(ExtraDamage),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier(HandCount),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier(HandCount)
        };

        public decimal InstanceMultiplier(CombatState cs, CardInstance card, CreatureState? target) => 1m;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }

        public override void OnUpgrade(CardInstance card)
        {
            card.Vars.CalculationBase.UpgradeValueBy(BaseUpgrade);
        }
    }

    private sealed class StarCard : CardDef
    {
        public const int Stars = 3;

        public StarCard()
            : base("CARD_KERNEL_TEST_STAR", 0, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }

        public override int CanonicalStarCost => Stars;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class EnergyXCard : CardDef
    {
        public EnergyXCard()
            : base("CARD_KERNEL_TEST_ENERGY_X", 0, CardType.Attack, CardRarity.Common, TargetType.AllEnemies, costsX: true)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class StarXCard : CardDef
    {
        public StarXCard()
            : base("CARD_KERNEL_TEST_STAR_X", 0, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }

        public override bool HasStarCostX => true;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class ListenerCardDef : CardDef
    {
        public ListenerCardDef()
            : base("CARD_KERNEL_TEST_LISTENER", 1, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }

        public override AbstractModel? CreateListener(CardInstance card) => new CountingListener(card);

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class SovereignBladeStateProbe : CardDef
    {
        public SovereignBladeStateProbe()
            : base("CARD_KERNEL_TEST_LOADED", 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
        {
        }

        public override int CanonicalStarCost => 1;

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
        {
            new DamageVar(4m, ValueProp.Move)
        };

        public override AbstractModel? CreateListener(CardInstance card) => new CountingListener(card);

        public override CardExtraState? CreateExtraState() => new ProbeState();

        public void Touch(CardInstance card) => ((ProbeState)card.Extra!).Value++;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }

        private sealed class ProbeState : CardExtraState
        {
            public int Value { get; set; }

            public override CardExtraState DeepClone() => (ProbeState)MemberwiseClone();

            public override void WriteCanonical(CanonicalWriter w)
            {
                w.WriteTag(0xCAF0);
                w.WriteInt32(Value);
            }
        }
    }

    private sealed class CountingListener : CardListener, ICanonical
    {
        public CountingListener(CardInstance card, int count = 0)
            : base(card)
        {
            Count = count;
        }

        public int Count { get; set; }

        public override AbstractModel CloneFor(CardInstance newOwner) => new CountingListener(newOwner, Count);

        public void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt32(Count);
        }
    }

    private sealed class OtherListener : CardListener
    {
        public OtherListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new OtherListener(newOwner);
    }

    private sealed class PlainEnchantment : EnchantmentModel
    {
        public override string Id => "CARD_KERNEL_TEST_PLAIN_ENCHANTMENT";
    }

    private sealed class ReplayEnchantment : EnchantmentModel
    {
        public override string Id => "CARD_KERNEL_TEST_REPLAY_ENCHANTMENT";

        public override int EnchantPlayCount(int originalPlayCount) => originalPlayCount + 5;
    }

    private sealed class RecordingEnchantment : EnchantmentModel
    {
        private readonly List<string> _log;
        private readonly bool _stackable;
        private readonly CardType? _only;
        private readonly string _tag;

        public RecordingEnchantment(List<string> log, bool stackable = false, CardType? only = null, string tag = "E")
        {
            _log = log;
            _stackable = stackable;
            _only = only;
            _tag = tag;
        }

        public override string Id => "CARD_KERNEL_TEST_ENCHANTMENT";

        public override bool IsStackable => _stackable;

        public override bool CanEnchantCardType(CardType cardType) => _only == null || cardType == _only;

        protected override void OnEnchant() => _log.Add("OnEnchant");

        public override void RecalculateValues() => _log.Add("RecalculateValues");

        public override void AfterCardEnteredCombat(CombatState cs, CardInstance card) => _log.Add(_tag + ":AfterCardEnteredCombat");
    }

    private sealed class RecordingAffliction : AfflictionModel
    {
        private readonly List<string> _log;
        private readonly bool _stackable;
        private readonly CardType? _only;
        private readonly bool _unplayable;
        private readonly string _tag;

        public RecordingAffliction(List<string> log, bool stackable = false, CardType? only = null, bool unplayable = true, string tag = "A")
        {
            _log = log;
            _stackable = stackable;
            _only = only;
            _unplayable = unplayable;
            _tag = tag;
        }

        public override string Id => "CARD_KERNEL_TEST_AFFLICTION";

        public override bool IsStackable => _stackable;

        public override bool CanAfflictUnplayableCards => _unplayable;

        public override bool CanAfflictCardType(CardType cardType) => _only == null || cardType == _only;

        public override void AfterApplied() => _log.Add("AfterApplied");

        public override void BeforeRemoved() => _log.Add("BeforeRemoved");

        public override void AfterCardEnteredCombat(CombatState cs, CardInstance card) => _log.Add(_tag + ":AfterCardEnteredCombat");
    }

    private sealed class AddKeywordPower : PowerModel
    {
        private readonly CardKeyword _keyword;
        private readonly string _id;

        public AddKeywordPower(CardKeyword keyword, string id = "CARD_KERNEL_TEST_KEYWORD_POWER")
        {
            _keyword = keyword;
            _id = id;
        }

        public override string Id => _id;

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)
        {
            keywords.Add(_keyword);
            return true;
        }
    }

    private sealed class FreeEnergyPower : PowerModel
    {
        public override string Id => "CARD_KERNEL_TEST_FREE_ENERGY_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
        {
            modifiedCost = 0m;
            return true;
        }
    }

    private sealed class PayWithStarsPower : PowerModel
    {
        public override string Id => "CARD_KERNEL_TEST_PAY_WITH_STARS_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player) => ReferenceEquals(player, Owner);
    }

    private sealed class IdPower : PowerModel
    {
        private readonly string _id;

        public IdPower(string id)
        {
            _id = id;
        }

        public override string Id => _id;

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    }

    private sealed class PickFirstAnswerer : IChoiceAnswerer
    {
        public ChoiceAnswer Answer(ChoicePrompt prompt) => new(prompt.Options.Count > 0 ? new[] { 0 } : Array.Empty<int>(), false);
    }
}
