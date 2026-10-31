using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class CardUpgradeTests
{
    private const string Encounter = NibbitsWeak.EncounterIdEntry;
    private const int A10 = 10;
    private const int A10Hp = 64;
    private const int IroncladMaxHp = 80;
    private const int SeedSearchLimit = 500;
    private const int TraceStepCap = 400;

    public static void Test_Cards_UpgradedValuesMatchTheModelUpgrades()
    {
        Assert.Equal(9m, StrikeIronclad.UpgradedDamage);
        Assert.Equal(8m, DefendIronclad.UpgradedBlock);
        Assert.Equal(10m, Bash.UpgradedDamage);
        Assert.Equal(3m, Bash.UpgradedVulnerable);

        var cs = NewCombat();
        var strike = CardInstance.Create(cs, new CardSpec(StrikeIronclad.CardId, false));
        var strikePlus = CardInstance.Create(cs, new CardSpec(StrikeIronclad.CardId, true));
        var defendPlus = CardInstance.Create(cs, new CardSpec(DefendIronclad.CardId, true));
        var bashPlus = CardInstance.Create(cs, new CardSpec(Bash.CardId, true));
        Assert.SequenceEqual(new[] { 0, 1, 2, 3 }, new[] { strike.InstanceId, strikePlus.InstanceId, defendPlus.InstanceId, bashPlus.InstanceId });

        Assert.False(strike.IsUpgraded);
        Assert.Equal(0, strike.UpgradeLevel);
        Assert.Equal(StrikeIronclad.BaseDamage, strike.Vars.Damage.BaseValue);
        Assert.Equal(new CardSpec(StrikeIronclad.CardId, false), strike.Spec);

        Assert.True(strikePlus.IsUpgraded);
        Assert.Equal(1, strikePlus.UpgradeLevel);
        Assert.False(strikePlus.IsUpgradable);
        Assert.Equal(StrikeIronclad.UpgradedDamage, strikePlus.Vars.Damage.BaseValue);
        Assert.Equal(new CardSpec(StrikeIronclad.CardId, true), strikePlus.Spec);

        Assert.Equal(DefendIronclad.UpgradedBlock, defendPlus.Vars.Block.BaseValue);
        Assert.Equal(ValueProp.Move, defendPlus.Vars.Block.Props);
        Assert.Equal(Bash.UpgradedDamage, bashPlus.Vars.Damage.BaseValue);
        Assert.Equal(Bash.UpgradedVulnerable, bashPlus.Vars.Vulnerable.BaseValue);

        foreach (var (card, cost) in new[] { (strikePlus, 1), (defendPlus, 1), (bashPlus, 2) })
        {
            Assert.Equal(cost, card.EnergyCost.Canonical, $"{card.Spec} canonical cost");
            Assert.Equal(cost, card.EnergyCost.Base, $"{card.Spec} keeps its cost");
            Assert.Equal(cost, card.EnergyCost.GetResolved(cs, card), $"{card.Spec} resolved cost");
            Assert.False(card.EnergyCost.WasJustUpgraded, $"{card.Spec} cost upgrade is finalized");
            foreach (var v in card.Vars.All)
            {
                Assert.False(v.WasJustUpgraded, $"{card.Spec} {v.Name} upgrade is finalized");
            }
        }
    }

    public static void Test_Cards_UpgradeIsPartOfTheCanonicalCard()
    {
        foreach (var id in new[] { StrikeIronclad.CardId, DefendIronclad.CardId, Bash.CardId })
        {
            var def = CardRegistry.Get(id);
            var plain = new CardInstance(def, 7);
            var viaSpec = CardInstance.Create(NewCombatWithNextCardId(7), new CardSpec(id, true));
            var viaSaveLoad = new CardInstance(def, 7);
            viaSaveLoad.UpgradeInternal();
            viaSaveLoad.FinalizeUpgradeInternal();
            Assert.Equal(7, viaSpec.InstanceId);
            Assert.SequenceEqual(CanonicalBytes(viaSaveLoad), CanonicalBytes(viaSpec), $"{id}+ via spec = UpgradeInternal + FinalizeUpgradeInternal");
            Assert.NotEqual(StateHasher.Hash(plain), StateHasher.Hash(viaSpec), $"{id}: the upgrade changes the card hash");
            Assert.Equal(StateHasher.Hash(viaSpec), StateHasher.Hash(viaSpec.DeepClone()), $"{id}+ clone hash");
            Assert.True(viaSpec.DeepClone().IsUpgraded);
        }
    }

    public static void Test_Cards_CardSpecParsesAndFormatsTheUpgradeSuffix()
    {
        Assert.Equal(new CardSpec("BASH", false), CardSpec.Parse("BASH"));
        Assert.Equal(new CardSpec("BASH", true), CardSpec.Parse("BASH+"));
        Assert.Equal("BASH", new CardSpec("BASH", false).ToString());
        Assert.Equal("STRIKE_IRONCLAD+", new CardSpec("STRIKE_IRONCLAD", true).ToString());
        foreach (var text in new[] { "GREED", "DEFEND_IRONCLAD+", "STRIKE_IRONCLAD" })
        {
            Assert.Equal(text, CardSpec.Parse(text).ToString());
        }
        Assert.Throws<FormatException>(() => CardSpec.Parse("+"));
        Assert.Throws<FormatException>(() => CardSpec.Parse("BASH++"));
        Assert.Throws<ArgumentException>(() => CardSpec.Parse(""));
        Assert.Throws<ArgumentNullException>(() => CardSpec.Parse(null!));

        var specs = CardSpec.FromIds(StartingDecks.Ironclad);
        Assert.Equal(StartingDecks.Ironclad.Count, specs.Length);
        for (int i = 0; i < specs.Length; i++)
        {
            Assert.Equal(new CardSpec(StartingDecks.Ironclad[i], false), specs[i]);
        }
        Assert.Throws<ArgumentNullException>(() => CardSpec.FromIds(null!));
    }

    public static void Test_Cards_InvalidSpecsAreRejectedBeforeAnIdIsAllocated()
    {
        var cs = NewCombat();
        Assert.Throws<ArgumentException>(() => CardInstance.Create(cs, new CardSpec(Greed.CardId, true)));
        Assert.Throws<ArgumentException>(() => CardInstance.Create(cs, new CardSpec(AscendersBane.CardId, true)));
        Assert.Throws<ArgumentException>(() => CardInstance.Create(cs, default(CardSpec)));
        Assert.Throws<KeyNotFoundException>(() => CardInstance.Create(cs, new CardSpec("NOT_A_CARD", false)));
        Assert.Equal(0, cs.NextCardInstanceId, "a rejected spec allocates no instance id");
        Assert.Equal(0, CardInstance.Create(cs, new CardSpec(Greed.CardId, false)).InstanceId);

        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        Assert.Throws<ArgumentNullException>(() => CombatSim.Create((RunRngSet)null!, deck, Encounter, 0, null, null));
        Assert.Throws<ArgumentNullException>(() => CombatSim.Create(new RunRngSet(1UL), (IReadOnlyList<CardSpec>)null!, Encounter, 0, null, null));
        Assert.Throws<ArgumentException>(() => CombatSim.Create(new RunRngSet(1UL), deck.Append(new CardSpec(Greed.CardId, true)).ToArray(), Encounter, 0, null, null));
        Assert.Throws<KeyNotFoundException>(() => CombatSim.Create(new RunRngSet(1UL), deck.Append(new CardSpec("NOT_A_CARD", false)).ToArray(), Encounter, 0, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSim.Create(new RunRngSet(1UL), deck, Encounter, 0, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSim.Create(new RunRngSet(1UL), deck, Encounter, 0, 81, 80));
    }

    public static void Test_Cards_SpecDeckKeepsDeckOrderAndTheIdDeckShuffle()
    {
        var deck = TalismanDeck(A10);
        Assert.SequenceEqual(
            new[] { "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD+", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD+", "BASH", "ASCENDERS_BANE" },
            deck.Select(s => s.ToString()));
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var rng = new RunRngSet(seed);
            var rngBefore = RngBytes(rng);
            var upgraded = CombatSim.Create(rng, deck, Encounter, A10, A10Hp, IroncladMaxHp);
            Assert.SequenceEqual(rngBefore, RngBytes(rng), "Create works on a copy of the streams");
            var plain = CombatSim.Create(new RunRngSet(seed), StartingDecks.Ironclad, Encounter, A10, A10Hp, IroncladMaxHp);

            Assert.SequenceEqual(IdLayout(plain.State), IdLayout(upgraded.State), $"seed {seed}: the same card ids in the same places");
            Assert.SequenceEqual(RngBytes(plain.State.Rng), RngBytes(upgraded.State.Rng), $"seed {seed}: upgrades consume no randomness");
            Assert.SequenceEqual(plain.LegalActions().ToArray(), upgraded.LegalActions().ToArray(), $"seed {seed}: Strike+ and Defend+ keep their cost");

            var cards = CombatCards(upgraded.State).OrderBy(c => c.InstanceId).ToList();
            Assert.Equal(deck.Length, cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                Assert.Equal(cards[0].InstanceId + i, cards[i].InstanceId, $"seed {seed}: instance ids are dealt in deck order");
                Assert.Equal(deck[i], cards[i].Spec, $"seed {seed}: instance {i} is deck entry {i}");
            }
            Assert.Equal(2, cards.Count(c => c.IsUpgraded));

            Assert.NotEqual(plain.StateHash(), upgraded.StateHash(), $"seed {seed}: the upgrade flag is in the state hash");
            Assert.Equal(upgraded.StateHash(), upgraded.Clone().StateHash());
            Assert.Equal(upgraded.StateHash(), CombatSim.Create(new RunRngSet(seed), deck, Encounter, A10, A10Hp, IroncladMaxHp).StateHash());
        }
    }

    public static void Test_Cards_IdOverloadsEqualAllUnupgradedSpecDecks()
    {
        foreach (int ascension in new[] { 0, 2, 4, 5, 10 })
        {
            int hp = StartingDecks.IroncladHpAfterNeowArrival(ascension);
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var byIds = CombatSim.Create(new RunRngSet(seed), StartingDecks.Ironclad, Encounter, ascension, hp, IroncladMaxHp);
                var bySpecs = CombatSim.Create(new RunRngSet(seed), CardSpec.FromIds(StartingDecks.IroncladAtRunStart(ascension)), Encounter, ascension, hp, IroncladMaxHp);
                Assert.Equal(byIds.StateHash(), bySpecs.StateHash(), $"A{ascension} seed {seed}: opening state");
                Assert.SequenceEqual(Trace(byIds, RandomPolicy(seed)), Trace(bySpecs, RandomPolicy(seed)), $"A{ascension} seed {seed}: whole combat");
            }
        }

        var bySeed = CombatSim.Create(3UL, StartingDecks.Ironclad, Encounter);
        var bySpecRng = CombatSim.Create(new RunRngSet(3UL), CardSpec.FromIds(StartingDecks.Ironclad), Encounter, 0, null, null);
        Assert.Equal(bySeed.StateHash(), bySpecRng.StateHash(), "default HP is the character's starting HP in both overloads");

        var verbatim = CombatSim.Create(new RunRngSet(1UL), CardSpec.FromIds(StartingDecks.Ironclad), Encounter, A10, A10Hp, IroncladMaxHp);
        Assert.Equal(10, CombatCards(verbatim.State).Count(), "the spec overload uses the deck exactly as given");
        Assert.Equal(0, CombatCards(verbatim.State).Count(c => c.Id == AscendersBane.CardId));
    }

    public static void Test_Cards_UpgradedStarterCardsHitHarderInCombat()
    {
        var upgradedDeck = CardSpec.FromIds(StartingDecks.Ironclad).Select(s => s with { Upgraded = true }).ToArray();
        var plainDeck = CardSpec.FromIds(StartingDecks.Ironclad);
        for (ulong seed = 1; seed <= SeedSearchLimit; seed++)
        {
            var up = CombatSim.Create(new RunRngSet(seed), upgradedDeck, Encounter, 0, null, null);
            var plain = CombatSim.Create(new RunRngSet(seed), plainDeck, Encounter, 0, null, null);
            if (HandIndex(up, Bash.CardId) < 0 || HandIndex(up, StrikeIronclad.CardId) < 0)
            {
                continue;
            }
            var upNibbit = up.State.Monsters[0];
            var plainNibbit = plain.State.Monsters[0];
            int startHp = upNibbit.CurrentHp;
            Assert.Equal(startHp, plainNibbit.CurrentHp);

            PlayFirst(up, Bash.CardId, 0);
            PlayFirst(plain, Bash.CardId, 0);
            Assert.Equal(startHp - 10, upNibbit.CurrentHp, "Bash+ deals 10");
            Assert.Equal(startHp - 8, plainNibbit.CurrentHp, "Bash deals 8");
            Assert.Equal(3, upNibbit.GetPowerAmount<VulnerablePower>(), "Bash+ applies 3 Vulnerable");
            Assert.Equal(2, plainNibbit.GetPowerAmount<VulnerablePower>(), "Bash applies 2 Vulnerable");
            Assert.Equal(1, up.State.Player.Energy, "Bash+ still costs 2");

            PlayFirst(up, StrikeIronclad.CardId, 0);
            PlayFirst(plain, StrikeIronclad.CardId, 0);
            Assert.Equal(startHp - 10 - (int)(StrikeIronclad.UpgradedDamage * 1.5m), upNibbit.CurrentHp, "Strike+ into Vulnerable");
            Assert.Equal(startHp - 8 - (int)(StrikeIronclad.BaseDamage * 1.5m), plainNibbit.CurrentHp, "Strike into Vulnerable");
            Assert.Equal(0, up.State.Player.Energy, "Strike+ still costs 1");

            up.Step(GameAction.EndTurn);
            plain.Step(GameAction.EndTurn);
            Assert.Equal(CombatOutcome.InProgress, up.Outcome);
            Assert.Equal(plain.State.Player.CurrentHp, up.State.Player.CurrentHp, "the enemy turn is the same");
            Assert.Equal(2, upNibbit.GetPowerAmount<VulnerablePower>(), "3 Vulnerable ticks down to 2");
            Assert.Equal(1, plainNibbit.GetPowerAmount<VulnerablePower>(), "2 Vulnerable ticks down to 1");
            if (HandIndex(up, DefendIronclad.CardId) < 0)
            {
                continue;
            }
            Assert.Equal(0, up.State.Player.Block);
            PlayFirst(up, DefendIronclad.CardId, GameAction.NoIndex);
            PlayFirst(plain, DefendIronclad.CardId, GameAction.NoIndex);
            Assert.Equal((int)DefendIronclad.UpgradedBlock, up.State.Player.Block, "Defend+ gains 8 block");
            Assert.Equal((int)DefendIronclad.BaseBlock, plain.State.Player.Block, "Defend gains 5 block");
            return;
        }
        Assert.Fail($"no seed up to {SeedSearchLimit} opens with Bash and Strike and draws Defend on turn 2");
    }

    public static void Test_Cards_UpgradedDeckReplaysDeterministicallyThroughReshuffles()
    {
        var deck = TalismanDeck(A10);
        deck[9] = deck[9] with { Upgraded = true };
        int initialShuffleDraws = deck.Length - 1;
        int seedsWithReshuffle = 0;
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var sim = CombatSim.Create(new RunRngSet(seed), deck, Encounter, A10, A10Hp, IroncladMaxHp);
            Assert.Equal(initialShuffleDraws, sim.State.Rng.Shuffle.Counter, "the opening shuffle draws deck size - 1 times");
            var upgradedIds = CombatCards(sim.State).Where(c => c.IsUpgraded).Select(c => c.InstanceId).OrderBy(i => i).ToArray();
            Assert.Equal(3, upgradedIds.Length);

            var hashes = new List<ulong> { sim.StateHash() };
            var buffer = new List<GameAction>(16);
            for (int step = 0; !sim.IsTerminal && step < TraceStepCap; step++)
            {
                sim.LegalActions(buffer);
                var action = StridePolicy(step, buffer);
                var clone = sim.Clone();
                sim.Step(action);
                clone.Step(action);
                Assert.Equal(sim.StateHash(), clone.StateHash(), $"seed {seed} step {step}: a clone steps to the same state");
                hashes.Add(sim.StateHash());
                var cards = CombatCards(sim.State).ToList();
                Assert.Equal(deck.Length, cards.Count, $"seed {seed} step {step}: no card is lost");
                Assert.SequenceEqual(upgradedIds, cards.Where(c => c.IsUpgraded).Select(c => c.InstanceId).OrderBy(i => i), $"seed {seed} step {step}: upgrades stay on their instances");
            }
            Assert.True(sim.IsTerminal, $"seed {seed}: the combat ends within {TraceStepCap} decisions");
            if (sim.State.Rng.Shuffle.Counter > initialShuffleDraws)
            {
                seedsWithReshuffle++;
            }
            Assert.SequenceEqual(hashes, Trace(CombatSim.Create(new RunRngSet(seed), deck, Encounter, A10, A10Hp, IroncladMaxHp), StridePolicy), $"seed {seed}: a second run repeats every hash");
        }
        Assert.True(seedsWithReshuffle > 0, "some combats reshuffle the discard pile with upgraded cards in it");
    }

    public static void Test_Cards_GreedDefinition()
    {
        Assert.Equal("GREED", Greed.CardId);
        Assert.Equal(Greed.CardId, DeterministicHash.Slugify(nameof(Greed)));
        var def = CardRegistry.Get(Greed.CardId);
        Assert.True(def is Greed);
        Assert.Equal(-1, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Curse, def.Type);
        Assert.Equal(CardRarity.Curse, def.Rarity);
        Assert.Equal(TargetType.None, def.TargetType);
        Assert.SequenceEqual(new[] { CardKeyword.Eternal, CardKeyword.Unplayable }, def.CanonicalKeywords);
        Assert.Equal(0, def.CanonicalTags.Count);
        Assert.Equal(0, def.MaxUpgradeLevel);
        Assert.Equal(-1, def.CanonicalStarCost);
        Assert.True(def.CanBeGeneratedInCombat, "Greed keeps the default (Ascender's Bane overrides it)");
        Assert.False(def.CanBeGeneratedByModifiers);
        Assert.False(def.GainsBlock);
        Assert.False(def.IsBasicStrikeOrDefend);
        Assert.False(def.HasTurnEndInHandEffect);
        Assert.Equal(0, def.CreateVars().Count);

        var cs = NewCombat();
        var greed = CardInstance.Create(cs, Greed.CardId);
        Assert.True(greed.HasKeyword(CardKeyword.Eternal));
        Assert.True(greed.HasKeyword(CardKeyword.Unplayable));
        Assert.False(greed.HasKeyword(CardKeyword.Ethereal));
        Assert.Equal((1 << (int)CardKeyword.Eternal) | (1 << (int)CardKeyword.Unplayable), greed.KeywordMask);
        Assert.False(greed.IsRemovable);
        Assert.False(greed.IsUpgradable);
        greed.Upgrade();
        Assert.Equal(0, greed.UpgradeLevel, "Upgrade skips a card that is not upgradable");
        Assert.Equal(new CardSpec(Greed.CardId, false), greed.Spec);
        Assert.Equal(-1, greed.EnergyCost.Base);
        Assert.Equal(0, greed.EnergyCost.GetAmountToSpend(cs, greed), "a negative cost spends nothing");
        Assert.Equal(0, greed.EnergyCost.GetResolved(cs, greed));
        Assert.False(greed.CanPlay(cs, out var reason));
        Assert.Equal(UnplayableReason.HasUnplayableKeyword, reason);
        cs.Player.Energy = 0;
        Assert.False(greed.CanPlay(cs, out reason));
        Assert.Equal(UnplayableReason.HasUnplayableKeyword, reason, "cost -1 never makes it too expensive");
        Assert.Equal(new CardLocation(PileType.Discard, CardPilePosition.Bottom), greed.GetResultLocationForCardPlay(cs));
    }

    public static void Test_Cards_GreedIsDeadWeightInCombat()
    {
        var deck = CardSpec.FromIds(StartingDecks.IroncladAtRunStart(A10)).Append(new CardSpec(Greed.CardId, false)).ToArray();
        Assert.Equal(12, deck.Length);
        for (ulong seed = 1; seed <= SeedSearchLimit; seed++)
        {
            var sim = CombatSim.Create(new RunRngSet(seed), deck, Encounter, A10, A10Hp, IroncladMaxHp);
            int greedIndex = HandIndex(sim, Greed.CardId);
            int baneIndex = HandIndex(sim, AscendersBane.CardId);
            if (greedIndex < 0 || baneIndex < 0)
            {
                continue;
            }
            var greed = sim.State.Player.Hand[greedIndex];
            var bane = sim.State.Player.Hand[baneIndex];
            Assert.False(greed.CanPlay(sim.State, out var reason));
            Assert.Equal(UnplayableReason.HasUnplayableKeyword, reason);
            Assert.False(sim.LegalActions().Any(a => a.Kind == ActionKind.PlayCard && a.CardIndex == greedIndex), "Greed never appears in the legal actions");
            Assert.False(sim.IsLegal(GameAction.Play(greedIndex)));
            Assert.False(sim.IsLegal(GameAction.Play(greedIndex, 0)));
            ulong before = sim.StateHash();
            Assert.Throws<InvalidOperationException>(() => sim.Step(GameAction.Play(greedIndex)));
            Assert.Equal(before, sim.StateHash(), "a rejected play leaves the state unchanged");
            Assert.Equal(deck.Length - 1, sim.State.Rng.Shuffle.Counter, "Greed takes part in the opening shuffle like any card");

            sim.Step(GameAction.EndTurn);
            Assert.Equal(CombatOutcome.InProgress, sim.Outcome);
            Assert.True(sim.State.Player.DiscardPile.Contains(greed), "Greed is discarded at the end of the turn");
            Assert.False(sim.State.Player.ExhaustPile.Contains(greed));
            Assert.True(sim.State.Player.ExhaustPile.Contains(bane), "Ascender's Bane is Ethereal and exhausts");
            Assert.Equal(0, greed.UpgradeLevel);
            return;
        }
        Assert.Fail($"no seed up to {SeedSearchLimit} opens with both Greed and Ascender's Bane");
    }

    private static CardSpec[] TalismanDeck(int ascension)
    {
        var deck = CardSpec.FromIds(StartingDecks.IroncladAtRunStart(ascension));
        deck[4] = deck[4] with { Upgraded = true };
        deck[8] = deck[8] with { Upgraded = true };
        return deck;
    }

    private static GameAction StridePolicy(int step, IReadOnlyList<GameAction> legal) => legal[(step * 7 + 3) % legal.Count];

    private static Func<int, IReadOnlyList<GameAction>, GameAction> RandomPolicy(ulong seed)
    {
        var random = new System.Random(unchecked((int)seed) ^ 0x5EED);
        return (_, legal) => legal[random.Next(legal.Count)];
    }

    private static List<ulong> Trace(CombatSim sim, Func<int, IReadOnlyList<GameAction>, GameAction> policy)
    {
        var hashes = new List<ulong> { sim.StateHash() };
        var buffer = new List<GameAction>(16);
        for (int step = 0; !sim.IsTerminal && step < TraceStepCap; step++)
        {
            sim.LegalActions(buffer);
            sim.Step(policy(step, buffer));
            hashes.Add(sim.StateHash());
        }
        return hashes;
    }

    private static void PlayFirst(CombatSim sim, string cardId, int targetIndex)
    {
        int index = HandIndex(sim, cardId);
        Assert.True(index >= 0, $"{cardId} is in the hand");
        sim.Step(GameAction.Play(index, targetIndex));
    }

    private static int HandIndex(CombatSim sim, string cardId)
    {
        var hand = sim.State.Player.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Id == cardId)
            {
                return i;
            }
        }
        return -1;
    }

    private static IEnumerable<CardInstance> CombatCards(CombatState cs)
    {
        foreach (var pile in cs.Player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                yield return card;
            }
        }
    }

    private static List<string> IdLayout(CombatState cs)
    {
        var layout = new List<string>();
        foreach (var pile in cs.Player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                layout.Add($"{pile.Type}:{card.Id}#{card.InstanceId}");
            }
        }
        return layout;
    }

    private static byte[] RngBytes(RunRngSet rng)
    {
        var w = new CanonicalWriter();
        rng.WriteCanonical(w);
        return w.ToArray();
    }

    private static byte[] CanonicalBytes(CardInstance card)
    {
        var w = new CanonicalWriter();
        card.WriteCanonical(w);
        return w.ToArray();
    }

    private static CombatState NewCombat() => NewCombatWithNextCardId(0);

    private static CombatState NewCombatWithNextCardId(int nextCardId)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = StartingDecks.IroncladMaxEnergy;
        var cs = new CombatState(player, "CARD_UPGRADE_TESTS", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.NextCardInstanceId = nextCardId;
        cs.IsInProgress = true;
        return cs;
    }
}
