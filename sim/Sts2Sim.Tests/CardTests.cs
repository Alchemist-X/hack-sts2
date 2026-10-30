using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class CardTests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Cards_RegistryResolvesStarterIds()
    {
        Assert.Equal("STRIKE_IRONCLAD", StrikeIronclad.CardId);
        Assert.Equal("DEFEND_IRONCLAD", DefendIronclad.CardId);
        Assert.Equal("BASH", Bash.CardId);
        Assert.True(CardRegistry.Get(StrikeIronclad.CardId) is StrikeIronclad);
        Assert.True(CardRegistry.Get(DefendIronclad.CardId) is DefendIronclad);
        Assert.True(CardRegistry.Get(Bash.CardId) is Bash);
        Assert.True(ReferenceEquals(CardRegistry.Get(Bash.CardId), CardRegistry.Get(Bash.CardId)));
        foreach (var id in CardRegistry.AllIds)
        {
            Assert.Equal(id, CardRegistry.Get(id).Id);
        }
    }

    public static void Test_Cards_IdsFollowModelDbSlugify()
    {
        Assert.Equal(StrikeIronclad.CardId, DeterministicHash.Slugify(nameof(StrikeIronclad)));
        Assert.Equal(DefendIronclad.CardId, DeterministicHash.Slugify(nameof(DefendIronclad)));
        Assert.Equal(Bash.CardId, DeterministicHash.Slugify(nameof(Bash)));
        Assert.Equal(StartingDecks.IroncladCharacterId, DeterministicHash.Slugify("Ironclad"));
    }

    public static void Test_Cards_IroncladStartingDeckMatchesCharacterModel()
    {
        var expected = new[]
        {
            "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD",
            "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD",
            "BASH"
        };
        Assert.SequenceEqual(expected, StartingDecks.Ironclad);
        foreach (var id in StartingDecks.Ironclad)
        {
            Assert.True(CardRegistry.Defs.Contains(id), id);
        }
        Assert.Equal("IRONCLAD", StartingDecks.IroncladCharacterId);
        Assert.Equal(80, StartingDecks.IroncladStartingHp);
        Assert.Equal(3, StartingDecks.IroncladMaxEnergy);
    }

    public static void Test_Cards_RunStartDeckAndNeowHpFollowAscension()
    {
        for (int ascension = 0; ascension <= 4; ascension++)
        {
            Assert.SequenceEqual(StartingDecks.Ironclad, StartingDecks.IroncladAtRunStart(ascension), $"A{ascension}: no curse yet");
        }
        var withBane = StartingDecks.Ironclad.Append(AscendersBane.CardId).ToArray();
        for (int ascension = 5; ascension <= 10; ascension++)
        {
            Assert.SequenceEqual(withBane, StartingDecks.IroncladAtRunStart(ascension), $"A{ascension}: Ascender's Bane appended last");
        }
        Assert.SequenceEqual(withBane, StartingDecks.IroncladWithAscendersBane);
        Assert.SequenceEqual(new[] { Bash.CardId, AscendersBane.CardId }, StartingDecks.ApplyAscensionEffectsToDeck(new[] { Bash.CardId }, 5), "any deck gets the curse at its end");
        Assert.True(StartingDecks.IsIroncladStartingDeck(StartingDecks.Ironclad));
        Assert.True(StartingDecks.IsIroncladStartingDeck(StartingDecks.Ironclad.ToList()));
        Assert.False(StartingDecks.IsIroncladStartingDeck(withBane));
        Assert.False(StartingDecks.IsIroncladStartingDeck(StartingDecks.Ironclad.Reverse().ToArray()), "deck order is part of the starting deck");

        Assert.Equal(80, StartingDecks.IroncladHpAfterNeowArrival(0));
        Assert.Equal(80, StartingDecks.IroncladHpAfterNeowArrival(1));
        for (int ascension = 2; ascension <= 10; ascension++)
        {
            Assert.Equal(64, StartingDecks.IroncladHpAfterNeowArrival(ascension), $"A{ascension}: Weary Traveler heals 80% of max HP");
        }
        Assert.Equal(55, StartingDecks.HpAfterNeowArrival(69, 2), "(int)(69 * 0.8) = (int)55.2");
        Assert.Equal(69, StartingDecks.HpAfterNeowArrival(69, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartingDecks.HpAfterNeowArrival(0, 0));
    }

    public static void Test_Cards_AscendersBaneDefinition()
    {
        Assert.Equal(AscendersBane.CardId, DeterministicHash.Slugify(nameof(AscendersBane)));
        var def = CardRegistry.Get(AscendersBane.CardId);
        Assert.True(def is AscendersBane);
        Assert.Equal(-1, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Curse, def.Type);
        Assert.Equal(CardRarity.Curse, def.Rarity);
        Assert.Equal(TargetType.None, def.TargetType);
        Assert.SequenceEqual(new[] { CardKeyword.Eternal, CardKeyword.Unplayable, CardKeyword.Ethereal }, def.CanonicalKeywords);
        Assert.Equal(0, def.CanonicalTags.Count);
        Assert.Equal(0, def.MaxUpgradeLevel);
        Assert.False(def.CanBeGeneratedInCombat);
        Assert.False(def.CanBeGeneratedByModifiers);
        Assert.Equal(-1, def.CanonicalStarCost);
        Assert.False(def.HasTurnEndInHandEffect);

        var cs = NewCombat(3);
        var card = CardInstance.Create(cs, AscendersBane.CardId);
        cs.Player.Hand.AddInternal(card);
        Assert.True(card.HasKeyword(CardKeyword.Eternal));
        Assert.True(card.HasKeyword(CardKeyword.Unplayable));
        Assert.True(card.HasKeyword(CardKeyword.Ethereal));
        Assert.False(card.HasKeyword(CardKeyword.Exhaust));
        Assert.False(card.IsRemovable, "Eternal");
        Assert.False(card.IsUpgradable);
        Assert.Equal(0, card.Vars.Count);
        Assert.Equal(-1, card.EnergyCost.Base);
        Assert.Equal(0, card.EnergyCost.GetAmountToSpend(cs, card), "a negative cost spends nothing");
        Assert.False(card.CanPlay(cs, out var reason));
        Assert.Equal(UnplayableReason.HasUnplayableKeyword, reason);
    }

    public static void Test_Cards_StrikeIroncladDefinition()
    {
        var def = CardRegistry.Get(StrikeIronclad.CardId);
        Assert.Equal(1, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Attack, def.Type);
        Assert.Equal(CardRarity.Basic, def.Rarity);
        Assert.Equal(TargetType.AnyEnemy, def.TargetType);
        Assert.SequenceEqual(new[] { CardTag.Strike }, def.CanonicalTags);
        Assert.Equal(0, def.CanonicalKeywords.Count);
        Assert.Equal(1, def.MaxUpgradeLevel);
        Assert.Equal(-1, def.CanonicalStarCost);
        Assert.False(def.HasStarCostX);
        Assert.False(def.GainsBlock);
        Assert.True(def.IsBasicStrikeOrDefend);
        Assert.False(def.HasTurnEndInHandEffect);

        var card = new CardInstance(def, 0);
        Assert.Equal(1, card.Vars.Count);
        var damage = card.Vars.Damage;
        Assert.Equal("Damage", damage.Name);
        Assert.Equal(6m, damage.BaseValue);
        Assert.Equal(6, damage.IntValue);
        Assert.Equal(ValueProp.Move, damage.Props);
        Assert.False(damage.WasJustUpgraded);
        Assert.Equal(1, card.EnergyCost.Canonical);
        Assert.Equal(1, card.EnergyCost.Base);
        Assert.Equal(1, card.EnergyCost.GetResolved());
        Assert.Equal(0, card.KeywordMask);
        Assert.True(card.HasTag(CardTag.Strike));
        Assert.False(card.HasTag(CardTag.Defend));
        Assert.Equal(-1, card.BaseStarCost);
        Assert.Equal(-1, card.CurrentStarCost);
        Assert.Equal(0, card.UpgradeLevel);
        Assert.True(card.IsUpgradable);
    }

    public static void Test_Cards_DefendIroncladDefinition()
    {
        var def = CardRegistry.Get(DefendIronclad.CardId);
        Assert.Equal(1, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Skill, def.Type);
        Assert.Equal(CardRarity.Basic, def.Rarity);
        Assert.Equal(TargetType.Self, def.TargetType);
        Assert.SequenceEqual(new[] { CardTag.Defend }, def.CanonicalTags);
        Assert.Equal(0, def.CanonicalKeywords.Count);
        Assert.Equal(1, def.MaxUpgradeLevel);
        Assert.True(def.GainsBlock);
        Assert.True(def.IsBasicStrikeOrDefend);

        var card = new CardInstance(def, 0);
        Assert.Equal(1, card.Vars.Count);
        var block = card.Vars.Block;
        Assert.Equal("Block", block.Name);
        Assert.Equal(5m, block.BaseValue);
        Assert.Equal(ValueProp.Move, block.Props);
        Assert.Equal(1, card.EnergyCost.GetResolved());
        Assert.True(card.HasTag(CardTag.Defend));
        Assert.True(card.GainsBlock);
    }

    public static void Test_Cards_BashDefinition()
    {
        var def = CardRegistry.Get(Bash.CardId);
        Assert.Equal(2, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Attack, def.Type);
        Assert.Equal(CardRarity.Basic, def.Rarity);
        Assert.Equal(TargetType.AnyEnemy, def.TargetType);
        Assert.Equal(0, def.CanonicalTags.Count);
        Assert.Equal(0, def.CanonicalKeywords.Count);
        Assert.Equal(1, def.MaxUpgradeLevel);
        Assert.False(def.GainsBlock);
        Assert.False(def.IsBasicStrikeOrDefend);

        var card = new CardInstance(def, 0);
        Assert.Equal(2, card.Vars.Count);
        Assert.Equal(DynamicVarSet.DamageName, card.Vars.All[0].Name);
        Assert.Equal(DynamicVarSet.VulnerableName, card.Vars.All[1].Name);
        Assert.Equal("VulnerablePower", card.Vars.Vulnerable.Name);
        Assert.Equal(8m, card.Vars.Damage.BaseValue);
        Assert.Equal(ValueProp.Move, card.Vars.Damage.Props);
        Assert.Equal(2m, card.Vars.Vulnerable.BaseValue);
        Assert.Equal(ValueProp.None, card.Vars.Vulnerable.Props);
        Assert.Equal(2, card.EnergyCost.Canonical);
        Assert.Equal(2, card.EnergyCost.GetResolved());
    }

    public static void Test_Cards_UpgradeAppliesModelDeltas()
    {
        var strike = NewCard(StrikeIronclad.CardId);
        strike.Upgrade();
        Assert.Equal(9m, strike.Vars.Damage.BaseValue);
        Assert.Equal(1, strike.UpgradeLevel);
        Assert.True(strike.IsUpgraded);
        Assert.False(strike.IsUpgradable);
        Assert.False(strike.Vars.Damage.WasJustUpgraded);
        Assert.Equal(1, strike.EnergyCost.Base);

        var defend = NewCard(DefendIronclad.CardId);
        defend.Upgrade();
        Assert.Equal(8m, defend.Vars.Block.BaseValue);
        Assert.Equal(1, defend.EnergyCost.Base);

        var bash = NewCard(Bash.CardId);
        bash.Upgrade();
        Assert.Equal(10m, bash.Vars.Damage.BaseValue);
        Assert.Equal(3m, bash.Vars.Vulnerable.BaseValue);
        Assert.Equal(2, bash.EnergyCost.Base);

        bash.Upgrade();
        Assert.Equal(1, bash.UpgradeLevel);
        Assert.Equal(10m, bash.Vars.Damage.BaseValue);
        Assert.Equal(3m, bash.Vars.Vulnerable.BaseValue);
        Assert.Throws<InvalidOperationException>(() => bash.UpgradeLevel = 2);
        Assert.Throws<InvalidOperationException>(() => bash.UpgradeInternal());
    }

    public static void Test_Cards_UpgradeInternalKeepsJustUpgradedUntilFinalized()
    {
        var bash = NewCard(Bash.CardId);
        bash.UpgradeInternal();
        Assert.True(bash.Vars.Damage.WasJustUpgraded);
        Assert.True(bash.Vars.Vulnerable.WasJustUpgraded);
        Assert.False(bash.EnergyCost.WasJustUpgraded);
        bash.FinalizeUpgradeInternal();
        Assert.False(bash.Vars.Damage.WasJustUpgraded);
        Assert.False(bash.Vars.Vulnerable.WasJustUpgraded);
    }

    public static void Test_Cards_InstancesOfOneDefDoNotShareState()
    {
        var def = CardRegistry.Get(StrikeIronclad.CardId);
        var a = new CardInstance(def, 0);
        var b = new CardInstance(def, 1);
        a.Upgrade();
        a.EnergyCost.SetThisTurn(0);
        a.AddKeyword(CardKeyword.Retain);
        Assert.Equal(6m, b.Vars.Damage.BaseValue);
        Assert.Equal(1, b.EnergyCost.GetResolved());
        Assert.False(b.HasKeyword(CardKeyword.Retain));
        Assert.Equal(6m, def.CreateVars().Damage.BaseValue);
        Assert.False(ReferenceEquals(def.CreateVars().Damage, def.CreateVars().Damage));
    }

    public static void Test_Cards_DeepCloneIsIndependent()
    {
        var original = NewCard(Bash.CardId);
        original.EnergyCost.AddThisTurn(1);
        original.SetStarCostThisCombat(0);
        var clone = original.DeepClone();
        Assert.Equal(original.InstanceId, clone.InstanceId);
        Assert.True(ReferenceEquals(original.Def, clone.Def));
        Assert.False(ReferenceEquals(original.Vars, clone.Vars));
        Assert.False(ReferenceEquals(original.EnergyCost, clone.EnergyCost));
        Assert.Equal(3, clone.EnergyCost.GetResolved());
        Assert.Equal(1, clone.TemporaryStarCosts.Count);

        clone.Upgrade();
        clone.EndOfTurnCleanup();
        clone.AddKeyword(CardKeyword.Exhaust);
        clone.SetStarCostThisTurn(5);

        Assert.Equal(8m, original.Vars.Damage.BaseValue);
        Assert.Equal(2m, original.Vars.Vulnerable.BaseValue);
        Assert.Equal(0, original.UpgradeLevel);
        Assert.Equal(3, original.EnergyCost.GetResolved());
        Assert.False(original.HasKeyword(CardKeyword.Exhaust));
        Assert.Equal(1, original.TemporaryStarCosts.Count);

        Assert.Equal(10m, clone.Vars.Damage.BaseValue);
        Assert.Equal(2, clone.EnergyCost.GetResolved());
        Assert.Equal(2, clone.TemporaryStarCosts.Count);
    }

    public static void Test_Cards_CanonicalHashSurvivesCloneAndTracksMutation()
    {
        var card = NewCard(Bash.CardId);
        card.EnergyCost.SetThisTurn(1);
        card.SetStarCostUntilPlayed(0);
        ulong hash = StateHasher.Hash(card);
        var clone = card.DeepClone();
        Assert.Equal(hash, StateHasher.Hash(clone));
        clone.Upgrade();
        Assert.NotEqual(hash, StateHasher.Hash(clone));
        Assert.Equal(hash, StateHasher.Hash(card));
        Assert.NotEqual(hash, StateHasher.Hash(NewCard(Bash.CardId)));
        var def = CardRegistry.Get(StrikeIronclad.CardId);
        Assert.NotEqual(StateHasher.Hash(new CardInstance(def, 0)), StateHasher.Hash(new CardInstance(def, 1)));
    }

    public static void Test_Cards_EnergyCostLocalModifierLifetimes()
    {
        var cost = NewCard(StrikeIronclad.CardId).EnergyCost;
        cost.SetThisTurn(0);
        Assert.Equal(0, cost.GetResolved());
        Assert.True(cost.EndOfTurnCleanup());
        Assert.Equal(1, cost.GetResolved());
        Assert.False(cost.EndOfTurnCleanup());

        cost.AddUntilPlayed(1);
        Assert.Equal(2, cost.GetResolved());
        Assert.False(cost.EndOfTurnCleanup());
        Assert.True(cost.AfterCardPlayedCleanup());
        Assert.Equal(1, cost.GetResolved());

        cost.SetThisTurnOrUntilPlayed(0);
        Assert.Equal(0, cost.GetResolved());
        Assert.True(cost.AfterCardPlayedCleanup());
        Assert.Equal(1, cost.GetResolved());

        cost.SetThisCombat(3, reduceOnly: true);
        Assert.Equal(1, cost.GetResolved());
        cost.AddThisCombat(-5);
        Assert.Equal(0, cost.GetResolved());
        Assert.False(cost.EndOfTurnCleanup());
        Assert.False(cost.AfterCardPlayedCleanup());
        Assert.Equal(2, cost.LocalModifiers.Count);

        cost.AddThisTurn(0);
        Assert.Equal(2, cost.LocalModifiers.Count);

        var negative = new CardEnergyCost(-1, false);
        negative.SetThisTurn(0);
        Assert.Equal(0, negative.LocalModifiers.Count);
        negative.SetThisTurn(2);
        Assert.Equal(1, negative.LocalModifiers.Count);
        Assert.Equal(-1, negative.GetWithModifiers(CostModifiers.All));
        Assert.Equal(0, negative.GetResolved());
    }

    public static void Test_Cards_EnergyUpgradeByClampsAbsoluteModifiers()
    {
        var cost = new CardEnergyCost(2, false);
        cost.SetThisCombat(2);
        cost.AddThisCombat(1);
        cost.UpgradeBy(-1);
        Assert.Equal(1, cost.Base);
        Assert.True(cost.WasJustUpgraded);
        Assert.Equal(1, cost.LocalModifiers[0].Amount);
        Assert.Equal(1, cost.LocalModifiers[1].Amount);
        Assert.Equal(2, cost.GetResolved());
        cost.FinalizeUpgrade();
        Assert.False(cost.WasJustUpgraded);

        cost.UpgradeBy(-5);
        Assert.Equal(0, cost.Base);
        Assert.True(cost.WasJustUpgraded);
        Assert.Equal(0, cost.LocalModifiers[0].Amount);
        cost.FinalizeUpgrade();
        cost.UpgradeBy(0);
        Assert.False(cost.WasJustUpgraded);

        cost.ResetForDowngrade();
        Assert.Equal(2, cost.Base);
    }

    public static void Test_Cards_LocalCostModifierModify()
    {
        const LocalCostModifierExpiration turn = LocalCostModifierExpiration.EndOfTurn;
        Assert.Equal(0, new LocalCostModifier(0, LocalCostType.Absolute, turn, false).Modify(2));
        Assert.Equal(3, new LocalCostModifier(3, LocalCostType.Absolute, turn, false).Modify(2));
        Assert.Equal(2, new LocalCostModifier(3, LocalCostType.Absolute, turn, true).Modify(2));
        Assert.Equal(1, new LocalCostModifier(-1, LocalCostType.Relative, turn, false).Modify(2));
        Assert.Equal(3, new LocalCostModifier(1, LocalCostType.Relative, turn, false).Modify(2));
        Assert.Equal(2, new LocalCostModifier(1, LocalCostType.Relative, turn, true).Modify(2));
        Assert.Equal(1, new LocalCostModifier(-1, LocalCostType.Relative, turn, true).Modify(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCostModifier(1, LocalCostType.None, turn, false).Modify(2));
    }

    public static void Test_Cards_XCostEnergyCost()
    {
        var cs = NewCombat(3);
        AddMonster(cs);
        var card = new CardInstance(new TestXCostCard(), 7);
        var cost = card.EnergyCost;
        Assert.True(cost.CostsX);
        Assert.Equal(0, cost.Canonical);
        Assert.Equal(0, cost.Base);
        Assert.Equal(3, cost.GetAmountToSpend(cs, card));
        Assert.Throws<InvalidOperationException>(() => cost.GetAmountToSpend());
        cost.CapturedXValue = 3;
        Assert.Equal(3, cost.GetResolved());
        Assert.Equal(3, card.ResolveEnergyXValue(cs));
        cost.UpgradeBy(-1);
        Assert.Equal(0, cost.Base);
        Assert.False(cost.WasJustUpgraded);

        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        Assert.Throws<InvalidOperationException>(() => _ = strike.EnergyCost.CapturedXValue);
        Assert.Throws<InvalidOperationException>(() => strike.EnergyCost.CapturedXValue = 1);
        Assert.Throws<InvalidOperationException>(() => strike.ResolveEnergyXValue(cs));
        Assert.Throws<InvalidOperationException>(() => strike.ResolveStarXValue(cs));
    }

    public static void Test_Cards_StarCostTemporaryOverrides()
    {
        var card = new CardInstance(new TestStarCard(), 3);
        Assert.Equal(2, card.BaseStarCost);
        Assert.Equal(2, card.CurrentStarCost);
        card.SetStarCostThisCombat(1);
        card.SetStarCostUntilPlayed(-4);
        Assert.Equal(0, card.CurrentStarCost);
        Assert.Equal(1, card.GetStarCostThisCombat());
        card.SetStarCostThisTurn(3);
        Assert.Equal(3, card.CurrentStarCost);
        card.EndOfTurnCleanup();
        Assert.Equal(0, card.CurrentStarCost);
        card.AfterCardPlayedCleanup();
        Assert.Equal(1, card.CurrentStarCost);
        card.UpgradeStarCostBy(-2);
        Assert.Equal(0, card.BaseStarCost);
        Assert.True(card.WasStarCostJustUpgraded);
        Assert.Equal(0, card.TemporaryStarCosts.Count);
        Assert.Equal(0, card.CurrentStarCost);
        card.FinalizeUpgradeInternal();
        Assert.False(card.WasStarCostJustUpgraded);
        Assert.Throws<InvalidOperationException>(() => new CardInstance(new TestStarXCard(), 4).UpgradeStarCostBy(1));
    }

    public static void Test_Cards_SetToFreeAffectsEnergyAndKeepsNegativeStarCost()
    {
        var bash = NewCard(Bash.CardId);
        bash.SetToFreeThisTurn();
        Assert.Equal(0, bash.EnergyCost.GetResolved());
        Assert.Equal(1, bash.TemporaryStarCosts.Count);
        Assert.Equal(-1, bash.CurrentStarCost);
        bash.EndOfTurnCleanup();
        Assert.Equal(2, bash.EnergyCost.GetResolved());
        Assert.Equal(0, bash.TemporaryStarCosts.Count);

        bash.SetToFreeThisTurn();
        bash.AfterCardPlayedCleanup();
        Assert.Equal(2, bash.EnergyCost.GetResolved());
        Assert.Equal(0, bash.TemporaryStarCosts.Count);

        bash.SetToFreeThisCombat();
        bash.EndOfTurnCleanup();
        bash.AfterCardPlayedCleanup();
        Assert.Equal(0, bash.EnergyCost.GetResolved());
        Assert.Equal(1, bash.TemporaryStarCosts.Count);
        Assert.Equal(-1, bash.CurrentStarCost);
    }

    public static void Test_Cards_KeywordsAndSingleTurnFlags()
    {
        var strike = NewCard(StrikeIronclad.CardId);
        Assert.False(strike.ShouldRetainThisTurn);
        strike.GiveSingleTurnRetain();
        strike.GiveSingleTurnSly();
        strike.ExhaustOnNextPlay = true;
        Assert.True(strike.ShouldRetainThisTurn);
        Assert.True(strike.IsSlyThisTurn);
        strike.EndOfTurnCleanup();
        Assert.False(strike.ShouldRetainThisTurn);
        Assert.False(strike.IsSlyThisTurn);
        Assert.False(strike.ExhaustOnNextPlay);

        strike.AddKeyword(CardKeyword.Retain);
        strike.EndOfTurnCleanup();
        Assert.True(strike.ShouldRetainThisTurn);
        Assert.Equal(1 << (int)CardKeyword.Retain, strike.KeywordMask);
        strike.RemoveKeyword(CardKeyword.Retain);
        Assert.False(strike.HasKeyword(CardKeyword.Retain));
        Assert.Equal(0, strike.KeywordMask);

        Assert.True(strike.IsRemovable);
        strike.AddKeyword(CardKeyword.Eternal);
        Assert.False(strike.IsRemovable);
        strike.AddKeyword(CardKeyword.Sly);
        Assert.True(strike.IsSlyThisTurn);
    }

    public static void Test_Cards_ResultLocationForCardPlay()
    {
        var cs = NewCombat(3);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        Assert.Equal(new CardLocation(PileType.Discard, CardPilePosition.Bottom), strike.GetResultLocationForCardPlay(cs));

        strike.ExhaustOnNextPlay = true;
        Assert.Equal(new CardLocation(PileType.Exhaust, CardPilePosition.Bottom), strike.GetResultLocationForCardPlay(cs));
        Assert.False(strike.ExhaustOnNextPlay);
        Assert.Equal(PileType.Discard, strike.GetResultLocationForCardPlay(cs).PileType);

        strike.AddKeyword(CardKeyword.Exhaust);
        Assert.Equal(PileType.Exhaust, strike.GetResultLocationForCardPlay(cs).PileType);

        strike.IsDupe = true;
        Assert.Equal(new CardLocation(PileType.None, CardPilePosition.Bottom), strike.GetResultLocationForCardPlay(cs));

        var power = new CardInstance(new TestPowerCard(), 99);
        power.ExhaustOnNextPlay = true;
        Assert.Equal(new CardLocation(PileType.None, CardPilePosition.Bottom), power.GetResultLocationForCardPlay(cs));
        Assert.True(power.ExhaustOnNextPlay);
    }

    public static void Test_Cards_IsValidTargetFollowsTargetType()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        var defend = CardInstance.Create(cs, DefendIronclad.CardId);
        var power = new CardInstance(new TestPowerCard(), 99);

        Assert.False(strike.IsValidTarget(cs, null));
        Assert.True(strike.IsValidTarget(cs, monster));
        Assert.False(strike.IsValidTarget(cs, cs.Player));

        Assert.True(defend.IsValidTarget(cs, null));
        Assert.False(defend.IsValidTarget(cs, monster));
        Assert.False(defend.IsValidTarget(cs, cs.Player));
        Assert.True(power.IsValidTarget(cs, null));

        monster.CurrentHp = 0;
        Assert.False(strike.IsValidTarget(cs, monster));
    }

    public static void Test_Cards_CreateAllocatesSequentialInstanceIds()
    {
        var cs = NewCombat(3);
        int first = cs.NextCardInstanceId;
        var a = CardInstance.Create(cs, StrikeIronclad.CardId);
        var b = CardInstance.Create(cs, CardRegistry.Get(Bash.CardId));
        Assert.Equal(first, a.InstanceId);
        Assert.Equal(first + 1, b.InstanceId);
        Assert.Equal(first + 2, cs.NextCardInstanceId);
        Assert.Throws<KeyNotFoundException>(() => CardInstance.Create(cs, "NOT_A_CARD"));
        Assert.Equal(first + 2, cs.NextCardInstanceId);
    }

    public static void Test_Cards_DynamicVarSemantics()
    {
        var v = new DynamicVar("Big", 2_000_000_000m);
        Assert.Equal(999999999m, v.BaseValue);
        v.BaseValue = 5.9m;
        Assert.Equal(5, v.IntValue);
        v.UpgradeValueBy(1m);
        Assert.True(v.WasJustUpgraded);
        Assert.Equal(6.9m, v.BaseValue);
        var copy = v.Clone();
        copy.BaseValue = 1m;
        Assert.Equal(6.9m, v.BaseValue);
        Assert.True(copy.WasJustUpgraded);

        Assert.Throws<ArgumentException>(() => new DynamicVarSet(new DynamicVar[] { new DamageVar(1m, ValueProp.Move), new DamageVar(2m, ValueProp.Move) }));
        var set = new DynamicVarSet(new DynamicVar[] { new BlockVar(3m, ValueProp.Move) });
        Assert.Throws<KeyNotFoundException>(() => _ = set.Damage);
        Assert.Null(set.TryGet(DynamicVarSet.DamageName));
        Assert.True(set.ContainsKey(DynamicVarSet.BlockName));
    }

    public static void Test_Cards_AfterCardPlayedCleanupResetsPlayState()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        strike.SetCurrentTarget(monster);
        strike.CurrentPlayIndex = 2;
        strike.EnergyCost.AddUntilPlayed(1);
        Assert.Equal(monster.CombatId, strike.CurrentTargetId);
        Assert.True(ReferenceEquals(monster, strike.GetCurrentTarget(cs)));
        strike.AfterCardPlayedCleanup();
        Assert.Equal(CardInstance.NoTarget, strike.CurrentTargetId);
        Assert.Equal(0, strike.CurrentPlayIndex);
        Assert.Null(strike.GetCurrentTarget(cs));
        Assert.Equal(1, strike.EnergyCost.GetResolved());
        strike.SetCurrentTarget(null);
        Assert.Equal(CardInstance.NoTarget, strike.CurrentTargetId);
    }

    public static void Test_Cards_CanPlayChecksEnergyKeywordAndTarget()
    {
        var cs = NewCombat(1);
        var monster = AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        var bash = CardInstance.Create(cs, Bash.CardId);

        Assert.True(strike.CanPlay(cs, out var strikeReason));
        Assert.Equal(UnplayableReason.None, strikeReason);
        Assert.False(bash.CanPlay(cs, out var bashReason));
        Assert.Equal(UnplayableReason.EnergyCostTooHigh, bashReason);
        Assert.True(strike.CanPlayTargeting(cs, monster));
        Assert.False(strike.CanPlayTargeting(cs, null));
        Assert.True(strike.HasEnoughResources(cs, out _));

        cs.Player.Energy = 2;
        Assert.True(bash.CanPlay(cs));
        cs.Player.Energy = 0;
        Assert.False(bash.CanPlay(cs));
        bash.SetToFreeThisTurn();
        Assert.True(bash.CanPlay(cs));

        strike.AddKeyword(CardKeyword.Unplayable);
        cs.Player.Energy = 3;
        Assert.False(strike.CanPlay(cs, out var unplayableReason));
        Assert.Equal(UnplayableReason.HasUnplayableKeyword, unplayableReason);
    }

    public static void Test_Cards_CostsEnergyOrStars()
    {
        var cs = NewCombat(3);
        AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        Assert.True(strike.CostsEnergyOrStars(cs, false));
        Assert.True(strike.CostsEnergyOrStars(cs, true));
        strike.SetToFreeThisTurn();
        Assert.False(strike.CostsEnergyOrStars(cs, false));
        Assert.False(strike.CostsEnergyOrStars(cs, true));
        var star = new CardInstance(new TestStarCard(), 50);
        star.EnergyCost.SetThisCombat(0);
        Assert.True(star.CostsEnergyOrStars(cs, false));
        Assert.Equal(2, star.GetStarCostWithModifiers(cs));
    }

    public static void Test_Cards_GlobalStarCostAppliesOnlyToCardsInCombatPiles()
    {
        var cs = NewCombat(3);
        AddMonster(cs);
        PowerCmd.Apply<FreeStarCostPower>(cs, cs.Player, 1m, cs.Player, null);
        var star = new CardInstance(new TestStarCard(), 60);
        Assert.Equal(2, star.GetStarCostWithModifiers(cs), "a card outside the combat piles ignores the hook");
        cs.Player.Stars = 0;
        Assert.False(star.HasEnoughResources(cs, out var outsideReason));
        Assert.Equal(UnplayableReason.StarCostTooHigh, outsideReason);

        cs.Player.Hand.AddInternal(star);
        Assert.Equal(0, star.GetStarCostWithModifiers(cs));
        Assert.True(star.HasEnoughResources(cs, out _));
        Assert.True(star.CostsEnergyOrStars(cs, false));

        var strike = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), 61);
        cs.Player.Hand.AddInternal(strike);
        Assert.Equal(-1, strike.GetStarCostWithModifiers(cs), "a negative star cost is returned unchanged");
    }

    public static void Test_Cards_ExcessEnergyIsPaidWithStarsOnlyWhenAHookAllowsIt()
    {
        var cs = NewCombat(1);
        AddMonster(cs);
        var bash = CardInstance.Create(cs, Bash.CardId);
        cs.Player.Hand.AddInternal(bash);
        cs.Player.Stars = 5;
        Assert.False(bash.HasEnoughResources(cs, out var noHookReason));
        Assert.Equal(UnplayableReason.EnergyCostTooHigh, noHookReason);

        PowerCmd.Apply<PayEnergyWithStarsPower>(cs, cs.Player, 1m, cs.Player, null);
        Assert.True(bash.HasEnoughResources(cs, out _));
        Assert.True(bash.CanPlay(cs));
        cs.Player.Stars = 1;
        Assert.False(bash.HasEnoughResources(cs, out var starReason));
        Assert.Equal(UnplayableReason.StarCostTooHigh, starReason);
        cs.Player.Stars = 2;
        Assert.True(bash.HasEnoughResources(cs, out _));
        cs.Player.Energy = 0;
        Assert.False(bash.HasEnoughResources(cs, out var zeroEnergyReason));
        Assert.Equal(UnplayableReason.StarCostTooHigh, zeroEnergyReason);
        cs.Player.Stars = 4;
        Assert.True(bash.HasEnoughResources(cs, out _));
    }

    public static void Test_Cards_TargetedCardsRequireTarget()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        var bash = CardInstance.Create(cs, Bash.CardId);
        Assert.Throws<ArgumentNullException>(() => strike.OnPlay(cs, new CardPlay(strike, null)));
        Assert.Throws<ArgumentNullException>(() => bash.OnPlay(cs, new CardPlay(bash, null)));
        Assert.Throws<ArgumentException>(() => strike.OnPlay(cs, new CardPlay(bash, monster)));
        Assert.Equal(40, monster.CurrentHp);
    }

    public static void Test_Cards_StrikeIroncladDealsSixDamage()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        strike.OnPlay(cs, new CardPlay(strike, monster));
        Assert.Equal(34, monster.CurrentHp);
        Assert.Equal(80, cs.Player.CurrentHp);
        Assert.Equal(0, cs.Player.Block);
    }

    public static void Test_Cards_DefendIroncladGainsFiveBlock()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var defend = CardInstance.Create(cs, DefendIronclad.CardId);
        defend.OnPlay(cs, new CardPlay(defend, null));
        Assert.Equal(5, cs.Player.Block);
        Assert.Equal(40, monster.CurrentHp);
    }

    public static void Test_Cards_BashDealsEightAndAppliesTwoVulnerable()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs);
        var bash = CardInstance.Create(cs, Bash.CardId);
        bash.OnPlay(cs, new CardPlay(bash, monster));
        Assert.Equal(32, monster.CurrentHp);
        var vulnerable = Assert.NotNull(monster.GetPower<VulnerablePower>());
        Assert.Equal(2, vulnerable.Amount);
        Assert.Equal(cs.Player.CombatId, vulnerable.ApplierId);
        Assert.Null(cs.Player.GetPower<VulnerablePower>());
    }

    public static void Test_Cards_UpgradedStarterCardsUseUpgradedValues()
    {
        var cs = NewCombat(3);
        var monster = AddMonster(cs, 60);
        var strike = CardInstance.Create(cs, StrikeIronclad.CardId);
        strike.Upgrade();
        strike.OnPlay(cs, new CardPlay(strike, monster));
        Assert.Equal(51, monster.CurrentHp);

        var defend = CardInstance.Create(cs, DefendIronclad.CardId);
        defend.Upgrade();
        defend.OnPlay(cs, new CardPlay(defend, null));
        Assert.Equal(8, cs.Player.Block);

        var other = NewCombat(3);
        var target = AddMonster(other, 60);
        var bash = CardInstance.Create(other, Bash.CardId);
        bash.Upgrade();
        bash.OnPlay(other, new CardPlay(bash, target));
        Assert.Equal(50, target.CurrentHp);
        Assert.Equal(3, Assert.NotNull(target.GetPower<VulnerablePower>()).Amount);
    }

    private static CardInstance NewCard(string id) => new(CardRegistry.Get(id), 0);

    private static CombatState NewCombat(int energy)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = energy;
        var cs = new CombatState(player, "CARD_TESTS", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CARD_TESTS_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CARD_TESTS_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class TestPowerCard : CardDef
    {
        public TestPowerCard()
            : base("CARD_TESTS_POWER", 1, CardType.Power, CardRarity.Common, TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class TestXCostCard : CardDef
    {
        public TestXCostCard()
            : base("CARD_TESTS_X", 3, CardType.Attack, CardRarity.Common, TargetType.AllEnemies, costsX: true)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class TestStarCard : CardDef
    {
        public TestStarCard()
            : base("CARD_TESTS_STAR", 1, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }

        public override int CanonicalStarCost => 2;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class FreeStarCostPower : PowerModel
    {
        public override string Id => "CARD_TESTS_FREE_STAR_COST_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool TryModifyStarCost(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
        {
            modifiedCost = 0m;
            return true;
        }
    }

    private sealed class PayEnergyWithStarsPower : PowerModel
    {
        public override string Id => "CARD_TESTS_PAY_ENERGY_WITH_STARS_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player) => ReferenceEquals(player, Owner);
    }

    private sealed class TestStarXCard : CardDef
    {
        public TestStarXCard()
            : base("CARD_TESTS_STAR_X", 0, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }

        public override bool HasStarCostX => true;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }
}
