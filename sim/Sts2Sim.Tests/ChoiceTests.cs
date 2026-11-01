using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class ChoiceTests
{
    private const string TestEncounterId = "CHOICE_TEST_ENCOUNTER";
    private const string Defend = DefendIronclad.CardId;
    private const string Strike = StrikeIronclad.CardId;
    private const string OnPlayMethod = "OnPlay";
    private const int AnyNumber = 999999999;

    public static void Test_Choice_ExposedHandPromptCarriesSiteOptionsAndBounds()
    {
        var sim = NewSim(101UL);
        var card = AddToHand(sim, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        var others = HandWithout(sim, card);
        var player = sim.State.Player;
        var play = PlayOf(sim, card);
        sim.Step(play);

        var prompt = Assert.NotNull(sim.PendingChoice, "five other cards and one pick: the screen opens");
        Assert.Equal(ChoicePromptKind.HandSelect, prompt.Kind);
        Assert.Equal("CHOICE_TEST_DISCARD_ONE:OnPlay", prompt.SiteId);
        Assert.Equal("CHOICE_TEST_DISCARD_ONE", prompt.SourceId);
        Assert.Equal(card.InstanceId, prompt.SourceInstanceId);
        Assert.Equal(PileType.Hand, prompt.Pile!.Value);
        Assert.SequenceEqual(others.Select(c => c.InstanceId), prompt.Options.Select(o => o.InstanceId), "hand order");
        Assert.SequenceEqual(others.Select(c => c.Id), prompt.Options.Select(o => o.ModelId));
        Assert.SequenceEqual(others.Select(c => c.UpgradeLevel), prompt.Options.Select(o => o.UpgradeLevel));
        Assert.Equal(1, prompt.MinSelect);
        Assert.Equal(1, prompt.MaxSelect);
        Assert.False(prompt.RequireConfirm);
        Assert.False(prompt.CanSkip);
        Assert.False(prompt.Cancelable);
        Assert.True(prompt.Ordered);
        Assert.Equal(ChoiceResultType.CombatCard, prompt.ResultType);
        Assert.False(sim.IsTerminal);
        Assert.Equal(CombatOutcome.InProgress, sim.Outcome);
        Assert.True(sim.State.IsInProgress);
        Assert.False(sim.PendingFromBegin);
        Assert.Equal(play, sim.PendingAction!.Value);
        Assert.Equal(0, sim.PendingPicks.Count);
        Assert.True(sim.State.Player.PlayPile.Contains(card), "the observation is the partial state up to the prompt");
        Assert.SequenceEqual(Enumerable.Range(0, others.Count).Select(GameAction.Pick), sim.LegalActions(), "one pick completes the prompt: picks only, no Confirm");

        sim.Step(GameAction.Pick(2));
        Assert.Null(sim.PendingChoice);
        Assert.SequenceEqual(new[] { others[2].InstanceId, card.InstanceId }, Ids(sim.State.Player.DiscardPile), "the pick is discarded during the play, the played card after it");
        Assert.SequenceEqual(others.Where((_, i) => i != 2).Select(c => c.InstanceId), Ids(sim.State.Player.Hand));
        Assert.True(sim.LegalActions().Contains(GameAction.EndTurn));
        Assert.False(ReferenceEquals(player, sim.State.Player), "a completed prompt replaces the observation with the re-run state");
    }

    public static void Test_Choice_AutomaticSelectionsResolveWithoutAPrompt()
    {
        var single = NewSim(103UL);
        TrimHand(single, 1);
        var only = single.State.Player.Hand[0];
        var discard = AddToHand(single, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        var state = single.State;
        single.Step(PlayOf(single, discard));
        Assert.Null(single.PendingChoice, "one option and MinSelect 1 without manual confirmation: all options, no screen");
        Assert.True(ReferenceEquals(state, single.State), "a step without a prompt keeps the state object");
        Assert.SequenceEqual(new[] { only.InstanceId, discard.InstanceId }, Ids(single.State.Player.DiscardPile));

        var empty = NewSim(103UL);
        TrimHand(empty, 0);
        var lone = AddToHand(empty, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        empty.Step(PlayOf(empty, lone));
        Assert.Null(empty.PendingChoice, "no option: an empty selection");
        Assert.SequenceEqual(new[] { lone.InstanceId }, Ids(empty.State.Player.DiscardPile));

        var upgrade = NewSim(105UL);
        TrimHand(upgrade, 3);
        upgrade.State.Player.Hand[0].Upgrade();
        upgrade.State.Player.Hand[2].Upgrade();
        var upgradable = upgrade.State.Player.Hand[1];
        var upgrader = AddToHand(upgrade, new HandUpgradeCard("CHOICE_TEST_UPGRADE"));
        upgrade.Step(PlayOf(upgrade, upgrader));
        Assert.Null(upgrade.PendingChoice, "FromHandForUpgrade with one upgradable card returns it without a screen");
        Assert.SequenceEqual(new[] { upgradable.InstanceId }, Ids(upgrade.State.Player.ExhaustPile));

        var none = NewSim(105UL);
        TrimHand(none, 2);
        foreach (var c in none.State.Player.Hand.Cards)
        {
            c.Upgrade();
        }
        var noneUpgrader = AddToHand(none, new HandUpgradeCard("CHOICE_TEST_UPGRADE"));
        none.Step(PlayOf(none, noneUpgrader));
        Assert.Null(none.PendingChoice);
        Assert.Equal(0, none.State.Player.ExhaustPile.Count, "no upgradable card: null");

        var pile = NewSim(107UL);
        var drawOrder = SetDrawPile(pile, new RarityCard("CHOICE_TEST_B", CardRarity.Rare), new RarityCard("CHOICE_TEST_A", CardRarity.Common), new RarityCard("CHOICE_TEST_Z", CardRarity.Basic));
        var exhauster = AddToHand(pile, new PileExhaustCard("CHOICE_TEST_DRAW_EXHAUST", PileType.Draw, 3));
        pile.Step(PlayOf(pile, exhauster));
        Assert.Null(pile.PendingChoice, "three cards and MinSelect 3: automatic");
        Assert.SequenceEqual(drawOrder.Select(c => c.InstanceId), Ids(pile.State.Player.ExhaustPile), "an automatic pile selection keeps pile order, not the sorted option order (D12)");
    }

    public static void Test_Choice_ChooseACardScreenRulesAndForcedSingleOption()
    {
        var forced = NewSim(109UL);
        var one = AddToHand(forced, new GenerateChooseCard("CHOICE_TEST_CHOOSE_ONE", 1, canSkip: false));
        forced.Step(PlayOf(forced, one));
        var prompt = Assert.NotNull(forced.PendingChoice, "a choose-a-card screen with one card and no skip is still a decision");
        Assert.Equal(ChoicePromptKind.ChooseCard, prompt.Kind);
        Assert.Null(prompt.Pile);
        Assert.Equal(ChoiceResultType.Index, prompt.ResultType);
        Assert.Equal(1, prompt.MinSelect);
        Assert.Equal(1, prompt.MaxSelect);
        Assert.SequenceEqual(new[] { GameAction.Pick(0) }, forced.LegalActions());
        int generated = prompt.Options[0].InstanceId;
        forced.Step(GameAction.Pick(0));
        Assert.True(forced.State.Player.Hand.Cards.Any(c => c.InstanceId == generated), "the picked generated card joins the hand with the id it had in the prompt");

        var skippable = NewSim(109UL);
        var three = AddToHand(skippable, new GenerateChooseCard("CHOICE_TEST_CHOOSE_THREE", 3, canSkip: true));
        int handBefore = skippable.State.Player.Hand.Count;
        skippable.Step(PlayOf(skippable, three));
        var skipPrompt = Assert.NotNull(skippable.PendingChoice);
        Assert.Equal(0, skipPrompt.MinSelect);
        Assert.Equal(1, skipPrompt.MaxSelect);
        Assert.True(skipPrompt.CanSkip);
        Assert.False(skipPrompt.RequireConfirm);
        Assert.SequenceEqual(new[] { GameAction.Pick(0), GameAction.Pick(1), GameAction.Pick(2), GameAction.Confirm }, skippable.LegalActions(), "Confirm with no pick is the skip");
        var ids = skipPrompt.Options.Select(o => o.InstanceId).ToArray();
        Assert.SequenceEqual(ids.OrderBy(i => i), ids, "generation order");
        skippable.Step(GameAction.Confirm);
        Assert.Null(skippable.PendingChoice);
        Assert.Equal(handBefore - 1, skippable.State.Player.Hand.Count, "skipped: no card joins the hand");

        var cs = NewSim(111UL).State;
        var def = CardRegistry.Get(Strike);
        var four = Enumerable.Range(0, 4).Select(_ => cs.CreateCard(def, cs.Player)).ToList();
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromChooseACardScreen(cs, four, cs.Player, false, Source("CHOICE_TEST_DIRECT")), "more than three cards throws");
        using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            Assert.Null(CardSelectCmd.FromChooseACardScreen(cs, Array.Empty<CardInstance>(), cs.Player, false, Source("CHOICE_TEST_DIRECT")), "no card returns null before the pause signal");
        }
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        {
            Assert.Null(CardSelectCmd.FromChooseACardScreen(cs, Array.Empty<CardInstance>(), cs.Player, true, Source("CHOICE_TEST_DIRECT")));
        }
        Assert.Equal(0, cs.Run.Fidelity.Count, "the empty screen never paused");

        var recorder = new RecordingSelector();
        cs.Choice.PushSelector(recorder);
        var picked = CardSelectCmd.FromChooseACardScreen(cs, four.Take(2).ToList(), cs.Player, false, Source("CHOICE_TEST_DIRECT"));
        cs.Choice.PopSelector();
        Assert.True(ReferenceEquals(four[0], picked), "the selector's first pick");
        Assert.Equal(0, recorder.Prompts.Single().MinSelect, "the selector branch passes minSelect 0 whatever canSkip is");
    }

    public static void Test_Choice_DrawPileOptionsSortByRarityThenOrdinalIdStably()
    {
        var sim = NewSim(113UL);
        var lowerA = new RarityCard("CHOICE_TEST_a", CardRarity.Common);
        var cards = SetDrawPile(sim,
            new RarityCard("CHOICE_TEST_B", CardRarity.Rare),
            lowerA,
            new RarityCard("CHOICE_TEST_C", CardRarity.Common),
            lowerA,
            new RarityCard("CHOICE_TEST_Z", CardRarity.Basic));
        var rare = cards[0];
        var a1 = cards[1];
        var upperC = cards[2];
        var a2 = cards[3];
        var basic = cards[4];
        var expected = new[] { basic, upperC, a1, a2, rare };
        Assert.SequenceEqual(expected.Select(c => c.InstanceId), CardSelectCmd.SortByRarityThenId(sim.State.Player.DrawPile.Cards).Select(c => c.InstanceId),
            "rarity ordinal, then the model id compared ordinally ('C' before 'a'), equal cards in pile order");
        var drawBefore = Ids(sim.State.Player.DrawPile);
        var exhauster = AddToHand(sim, new PileExhaustCard("CHOICE_TEST_DRAW_EXHAUST", PileType.Draw, 1));
        sim.Step(PlayOf(sim, exhauster));
        var prompt = Assert.NotNull(sim.PendingChoice);
        Assert.Equal(ChoicePromptKind.PileSelect, prompt.Kind);
        Assert.Equal(PileType.Draw, prompt.Pile!.Value);
        Assert.SequenceEqual(expected.Select(c => c.InstanceId), prompt.Options.Select(o => o.InstanceId));
        Assert.SequenceEqual(drawBefore, Ids(sim.State.Player.DrawPile), "sorting the options leaves the draw order alone");
        sim.Step(GameAction.Pick(3));
        Assert.SequenceEqual(new[] { a2.InstanceId }, Ids(sim.State.Player.ExhaustPile), "the option index maps through the sorted order");

        var discard = NewSim(115UL);
        var discardCards = SetPile(discard.State.Player.DiscardPile, discard.State,
            new RarityCard("CHOICE_TEST_B", CardRarity.Rare),
            new RarityCard("CHOICE_TEST_Z", CardRarity.Basic));
        var fetch = AddToHand(discard, new PileExhaustCard("CHOICE_TEST_DISCARD_EXHAUST", PileType.Discard, 1));
        discard.Step(PlayOf(discard, fetch));
        var discardPrompt = Assert.NotNull(discard.PendingChoice);
        Assert.SequenceEqual(discardCards.Select(c => c.InstanceId), discardPrompt.Options.Select(o => o.InstanceId), "discard pile prompts keep pile order");
    }

    public static void Test_Choice_FromCombatPileRejectsNonCombatPilesAfterTheEndingCheck()
    {
        var cs = NewSim(117UL).State;
        var deckPile = new CardPile(PileType.Deck);
        var ex = Assert.Throws<InvalidOperationException>(() => CardSelectCmd.FromCombatPile(cs, deckPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1), Source("CHOICE_TEST_DIRECT")));
        Assert.Equal("Cannot perform on a non combat pile", ex.Message);
        using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            var again = Assert.Throws<InvalidOperationException>(() => CardSelectCmd.FromCombatPile(cs, deckPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1), Source("CHOICE_TEST_DIRECT")));
            Assert.Equal("Cannot perform on a non combat pile", again.Message, "the pile check precedes the pause signal");
        }
        cs.Monsters.Clear();
        Assert.True(CombatStatus.IsEnding(cs));
        Assert.Equal(0, CardSelectCmd.FromCombatPile(cs, deckPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1), Source("CHOICE_TEST_DIRECT")).Count(), "IsEnding returns empty before the pile check");
    }

    public static void Test_Choice_EarlyOutsUseIsEndingOrIsOverOrEndingPerMethod()
    {
        var ending = NewSim(119UL).State;
        var player = ending.Player;
        var grid = new[] { ending.CreateCard(CardRegistry.Get(Strike), player), ending.CreateCard(CardRegistry.Get(Strike), player) };
        ending.Monsters.Clear();
        Assert.True(CombatStatus.IsEnding(ending));
        using (ending.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            Assert.Equal(0, CardSelectCmd.FromHand(ending, player, new CardSelectorPrefs("K", 1), null, Source("CHOICE_TEST_DIRECT")).Count());
            Assert.Equal(0, CardSelectCmd.FromHandForDiscard(ending, player, new CardSelectorPrefs("K", 1), null, Source("CHOICE_TEST_DIRECT")).Count());
            Assert.Null(CardSelectCmd.FromHandForUpgrade(ending, player, Source("CHOICE_TEST_DIRECT")));
            Assert.Equal(0, CardSelectCmd.FromSimpleGrid(ending, grid, player, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_DIRECT")).Count());
            Assert.Equal(0, CardSelectCmd.FromCombatPile(ending, player.DrawPile, player, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_DIRECT")).Count());
            Assert.Throws<InvalidOperationException>(() => CardSelectCmd.FromChooseACardScreen(ending, grid, player, false, Source("CHOICE_TEST_DIRECT")), "FromChooseACardScreen has no early-out and pauses");
        }

        var over = NewSim(121UL).State;
        var overPlayer = over.Player;
        var overGrid = new[] { over.CreateCard(CardRegistry.Get(Strike), overPlayer), over.CreateCard(CardRegistry.Get(Strike), overPlayer) };
        over.IsInProgress = false;
        Assert.False(CombatStatus.IsEnding(over));
        Assert.True(CombatStatus.IsOverOrEnding(over));
        using (over.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            Assert.Equal(0, CardSelectCmd.FromHand(over, overPlayer, new CardSelectorPrefs("K", 1), null, Source("CHOICE_TEST_DIRECT")).Count(), "FromHand gates on IsOverOrEnding");
            Assert.Null(CardSelectCmd.FromHandForUpgrade(over, overPlayer, Source("CHOICE_TEST_DIRECT")), "FromHandForUpgrade gates on IsOverOrEnding");
            Assert.Throws<InvalidOperationException>(() => CardSelectCmd.FromSimpleGrid(over, overGrid, overPlayer, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_DIRECT")), "FromSimpleGrid gates on IsEnding only and pauses");
            Assert.Throws<InvalidOperationException>(() => CardSelectCmd.FromCombatPile(over, overPlayer.DrawPile, overPlayer, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_DIRECT")), "FromCombatPile gates on IsEnding only and pauses");
        }
    }

    public static void Test_Choice_PauseSignalPrecedesCountingAndSelectorsSuppressIt()
    {
        var cs = NewSim(123UL).State;
        var player = cs.Player;
        var log = cs.Run.Fidelity;
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        {
            Assert.Equal(0, CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 1), _ => false, Source("CHOICE_TEST_HAND")).Count());
        }
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        {
            Assert.Equal(0, CardSelectCmd.FromSimpleGrid(cs, Array.Empty<CardInstance>(), player, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_GRID")).Count());
        }
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        {
            Assert.Equal(0, CardSelectCmd.FromCombatPile(cs, player.ExhaustPile, player, new CardSelectorPrefs("K", 1), Source("CHOICE_TEST_PILE")).Count());
        }
        foreach (var c in player.Hand.Cards)
        {
            c.Upgrade();
        }
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        {
            Assert.Null(CardSelectCmd.FromHandForUpgrade(cs, player, Source("CHOICE_TEST_UPGRADE")));
        }
        Assert.SequenceEqual(new[] { "CHOICE_TEST_HAND:Probe", "CHOICE_TEST_GRID:Probe", "CHOICE_TEST_PILE:Probe", "CHOICE_TEST_UPGRADE:Probe" }, log.Entries.Select(e => e.Site),
            "FromHand, FromSimpleGrid, FromCombatPile and FromHandForUpgrade pause before counting, so empty selections still pause");
        Assert.True(log.Entries.All(e => e.Flag == FidelityFlag.DeferredListenerPromptInline));

        int steps = 0;
        player.Hand[0].UpgradeLevel = 0;
        using (var scope = cs.Choice.PushContext(ChoiceContextKind.TurnSetup, () => steps++))
        {
            Assert.True(ReferenceEquals(player.Hand[0], CardSelectCmd.FromHandForUpgrade(cs, player, Source("CHOICE_TEST_UPGRADE"))), "one upgradable card: automatic");
            Assert.Equal(1, steps, "an automatic selection still runs the turn-setup first-pause steps");
            Assert.True(scope.FirstPauseDone);
        }

        int suppressed = 0;
        using (cs.Choice.PushContext(ChoiceContextKind.TurnSetup, () => suppressed++))
        using (cs.Choice.PushSelectorScope(new VakuuCardSelector()))
        {
            CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 1), _ => false, Source("CHOICE_TEST_HAND"));
        }
        using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
        using (cs.Choice.PushSelectorScope(new VakuuCardSelector()))
        {
            CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 1), _ => false, Source("CHOICE_TEST_HAND"));
        }
        using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
        using (cs.Choice.PushSelectorScope(new VakuuCardSelector()))
        {
            CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 1), _ => false, Source("CHOICE_TEST_HAND"));
        }
        Assert.Equal(0, suppressed, "an installed selector suppresses the pause");
        Assert.Equal(4, log.Count, "no flag while a selector is installed");
        Assert.Equal(0, cs.Choice.Selectors.Count);
        Assert.Equal(0, cs.Choice.ContextDepth);
    }

    public static void Test_Choice_ChoiceSourceIsRequired()
    {
        var cs = NewSim(125UL).State;
        var player = cs.Player;
        var generated = new[] { cs.CreateCard(CardRegistry.Get(Strike), player) };
        var prefs = new CardSelectorPrefs("K", 1);
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromChooseACardScreen(cs, generated, player));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromSimpleGrid(cs, generated, player, prefs));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromCombatPile(cs, player.DrawPile, player, prefs));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromCombatPile(cs, player.DrawPile, player, prefs, null));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromHand(cs, player, prefs, null, default));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromHandForDiscard(cs, player, prefs, null, default));
        Assert.Throws<ArgumentException>(() => CardSelectCmd.FromHandForUpgrade(cs, player, default));
        var source = ChoiceSource.For("CHOICE_TEST_RELIC", ChoiceSource.NoInstance, "AfterPlayerTurnStart");
        Assert.Equal("CHOICE_TEST_RELIC:AfterPlayerTurnStart", source.SiteId);
        Assert.Equal(ChoiceSource.NoInstance, source.SourceInstanceId);
    }

    public static void Test_Choice_SelectionBoundsAreClippedAndApproximationsFlagged()
    {
        var cs = NewSim(127UL).State;
        var player = cs.Player;
        var answerer = ScriptedAnswerer.Of(new[] { 4, 0 });
        cs.Choice.Answerer = answerer;
        var any = CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 0, AnyNumber), null, Source("CHOICE_TEST_ANY")).ToList();
        var prompt = answerer.Prompts.Single();
        Assert.Equal(0, prompt.MinSelect);
        Assert.Equal(5, prompt.MaxSelect, "MaxSelect is clipped to the option count");
        Assert.True(prompt.RequireConfirm, "(0, 999999999) requires manual confirmation");
        Assert.SequenceEqual(new[] { player.Hand[4], player.Hand[0] }, any, "the result is in pick order");
        Assert.Equal(0, cs.Run.Fidelity.Count);

        var tooMany = ScriptedAnswerer.Of(new[] { 0, 1, 2, 3, 4 });
        cs.Choice.Answerer = tooMany;
        CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 7, 9), null, Source("CHOICE_TEST_MIN")).ToList();
        var clipped = tooMany.Prompts.Single();
        Assert.Equal(5, clipped.MinSelect);
        Assert.Equal(5, clipped.MaxSelect);
        Assert.Equal(CardSelectCmd.MinSelectClippedSite + "CHOICE_TEST_MIN:Probe", cs.Run.Fidelity.Entries.Single().Site, "the hand screen does not clip MinSelect, so the clip is flagged");
        Assert.Equal(FidelityFlag.ApproximatedRule, cs.Run.Fidelity.Entries.Single().Flag);

        var unused = ScriptedAnswerer.Of();
        cs.Choice.Answerer = unused;
        Assert.Equal(0, CardSelectCmd.FromHand(cs, player, new CardSelectorPrefs("K", 0), null, Source("CHOICE_TEST_ZERO")).Count(), "MaxSelect 0 without confirmation completes with no pick");
        Assert.Equal(0, unused.Prompts.Count, "no decision is asked for");
        Assert.Equal(CardSelectCmd.EmptySelectionWithoutDecisionSite + "CHOICE_TEST_ZERO:Probe", cs.Run.Fidelity.Entries[1].Site);
        cs.Choice.Answerer = null;
    }

    public static void Test_Choice_OrderedPicksGiveDistinctHashesAndResults()
    {
        var a = NewSim(129UL);
        var card = AddToHand(a, new HandDiscardCard("CHOICE_TEST_DISCARD_TWO", 2, 2));
        var others = HandWithout(a, card);
        a.Step(PlayOf(a, card));
        var b = a.Clone();
        ulong start = a.StateHash();
        Assert.Equal(start, b.StateHash());

        var observation = a.State;
        a.Step(GameAction.Pick(0));
        b.Step(GameAction.Pick(1));
        Assert.True(ReferenceEquals(observation, a.State), "a pick that does not complete the prompt re-runs nothing");
        Assert.SequenceEqual(new[] { 0 }, a.PendingPicks);
        Assert.NotEqual(start, a.StateHash(), "picks enter the hash");
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.SequenceEqual(new[] { 1, 2, 3, 4 }.Select(GameAction.Pick), a.LegalActions(), "ordered: every unpicked option");
        Assert.SequenceEqual(new[] { 0, 2, 3, 4 }.Select(GameAction.Pick), b.LegalActions());

        a.Step(GameAction.Pick(1));
        b.Step(GameAction.Pick(0));
        Assert.Null(a.PendingChoice);
        Assert.Null(b.PendingChoice);
        Assert.SequenceEqual(new[] { others[0].InstanceId, others[1].InstanceId, card.InstanceId }, Ids(a.State.Player.DiscardPile));
        Assert.SequenceEqual(new[] { others[1].InstanceId, others[0].InstanceId, card.InstanceId }, Ids(b.State.Player.DiscardPile));
        Assert.NotEqual(a.StateHash(), b.StateHash(), "the same set in another order is another result");
    }

    public static void Test_Choice_UnorderedPicksAreSortedBeforeTheTape()
    {
        var a = NewSim(131UL);
        var card = AddToHand(a, new UnorderedDiscardCard("CHOICE_TEST_UNORDERED"));
        var others = HandWithout(a, card);
        a.Step(PlayOf(a, card));
        var prompt = Assert.NotNull(a.PendingChoice);
        Assert.False(prompt.Ordered);
        Assert.SequenceEqual(new[] { 0, 1, 2, 3 }.Select(GameAction.Pick), a.LegalActions(), "two of five, ascending: the last option cannot start a pair");
        var b = a.Clone();

        a.Step(GameAction.Pick(0));
        Assert.SequenceEqual(new[] { 1, 2, 3, 4 }.Select(GameAction.Pick), a.LegalActions());
        a.Step(GameAction.Pick(1));

        b.Step(GameAction.Pick(1));
        Assert.SequenceEqual(new[] { 2, 3, 4 }.Select(GameAction.Pick), b.LegalActions(), "only options after the last pick are listed");
        Assert.True(b.IsLegal(GameAction.Pick(0)), "an earlier unpicked option stays legal");
        b.Step(GameAction.Pick(0));

        Assert.Null(a.PendingChoice);
        Assert.Null(b.PendingChoice);
        Assert.Equal(a.StateHash(), b.StateHash(), "transposed picks of an unordered prompt reach the same state");
        Assert.SequenceEqual(new[] { others[0].InstanceId, others[1].InstanceId, card.InstanceId }, Ids(b.State.Player.DiscardPile), "the answer is sorted");

        var c = NewSim(131UL);
        var chained = AddToHand(c, new UnorderedDiscardCard("CHOICE_TEST_UNORDERED_THEN_PILE", thenPile: true));
        c.Step(PlayOf(c, chained));
        var d = c.Clone();
        c.Step(GameAction.Pick(0));
        c.Step(GameAction.Pick(1));
        d.Step(GameAction.Pick(1));
        d.Step(GameAction.Pick(0));
        Assert.Equal(ChoicePromptKind.PileSelect, Assert.NotNull(c.PendingChoice).Kind, "a second prompt follows in the same play");
        Assert.SequenceEqual(new[] { 0, 1 }, c.PendingTape[0].Picks);
        Assert.SequenceEqual(new[] { 0, 1 }, d.PendingTape[0].Picks, "the answer is sorted before it enters the tape");
        Assert.Equal(c.StateHash(), d.StateHash(), "so transpositions hash equal at the next prompt too");
    }

    public static void Test_Choice_ConfirmAndCancelLegality()
    {
        var any = NewSim(133UL);
        var card = AddToHand(any, new HandDiscardCard("CHOICE_TEST_DISCARD_ANY", 0, AnyNumber));
        any.Step(PlayOf(any, card));
        var prompt = Assert.NotNull(any.PendingChoice);
        Assert.True(prompt.RequireConfirm);
        Assert.SequenceEqual(Enumerable.Range(0, 5).Select(GameAction.Pick).Append(GameAction.Confirm), any.LegalActions(), "Confirm at zero picks when MinSelect is 0");
        Assert.False(any.IsLegal(GameAction.Cancel), "not cancelable");
        Assert.False(any.IsLegal(new GameAction(ActionKind.ChooseConfirm, 0, GameAction.NoIndex)), "a Confirm with a card index is malformed");
        Assert.False(any.IsLegal(new GameAction(ActionKind.ChooseConfirm, GameAction.NoIndex, 0)), "a Confirm with a target index is malformed");
        Assert.False(any.IsLegal(new GameAction(ActionKind.ChoosePick, 0, 0)), "a Pick with a target index is malformed");
        for (int i = 4; i >= 0; i--)
        {
            any.Step(GameAction.Pick(i));
            Assert.NotNull(any.PendingChoice, "manual confirmation never completes on a pick");
            Assert.True(any.LegalActions().Contains(GameAction.Confirm));
        }
        Assert.SequenceEqual(new[] { GameAction.Confirm }, any.LegalActions(), "all five picked: only Confirm");
        Assert.False(any.IsLegal(GameAction.Pick(0)));
        any.Step(GameAction.Confirm);
        Assert.Null(any.PendingChoice);
        Assert.Equal(0, any.State.Player.Hand.Count);

        var fixedCount = NewSim(133UL);
        var one = AddToHand(fixedCount, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        fixedCount.Step(PlayOf(fixedCount, one));
        Assert.False(fixedCount.IsLegal(GameAction.Confirm), "a prompt without manual confirmation has no Confirm");
        Assert.Throws<InvalidOperationException>(() => fixedCount.Step(GameAction.Confirm));

        var noConfirm = NewSim(133UL);
        var upToTwo = AddToHand(noConfirm, new HandDiscardCard("CHOICE_TEST_DISCARD_UP_TO_TWO", 0, 2, requireConfirm: false));
        noConfirm.Step(PlayOf(noConfirm, upToTwo));
        var open = Assert.NotNull(noConfirm.PendingChoice);
        Assert.False(open.RequireConfirm, "an explicit RequireManualConfirmation = false with MinSelect < MaxSelect");
        Assert.False(noConfirm.IsLegal(GameAction.Confirm), "MinSelect <= picks is not enough: Confirm needs RequireConfirm or CanSkip");
        noConfirm.Step(GameAction.Pick(1));
        Assert.False(noConfirm.IsLegal(GameAction.Confirm));
        Assert.SequenceEqual(new[] { 0, 2, 3, 4 }.Select(GameAction.Pick), noConfirm.LegalActions());
        noConfirm.Step(GameAction.Pick(3));
        Assert.Null(noConfirm.PendingChoice, "the prompt completes at MaxSelect");

        var cancelable = NewSim(133UL);
        var cancelCard = AddToHand(cancelable, new HandDiscardCard("CHOICE_TEST_DISCARD_CANCEL", 1, 1, cancelable: true));
        int handBefore = cancelable.State.Player.Hand.Count;
        cancelable.Step(PlayOf(cancelable, cancelCard));
        Assert.True(cancelable.PendingChoice!.Cancelable);
        Assert.SequenceEqual(Enumerable.Range(0, 5).Select(GameAction.Pick).Append(GameAction.Cancel), cancelable.LegalActions(), "Cancel is listed last");
        Assert.False(cancelable.IsLegal(new GameAction(ActionKind.ChooseCancel, 0, GameAction.NoIndex)), "a Cancel with an index is malformed");
        Assert.False(cancelable.IsLegal(GameAction.Confirm), "Cancelable does not add a Confirm");
        var picked = cancelable.Clone();
        picked.Step(GameAction.Pick(0));
        Assert.Null(picked.PendingChoice, "the pick still completes the prompt");
        cancelable.Step(GameAction.Cancel);
        Assert.Null(cancelable.PendingChoice);
        Assert.True(cancelable.PendingTape.Count == 0);
        Assert.Equal(handBefore - 1, cancelable.State.Player.Hand.Count, "a cancelled selection selects nothing");
        Assert.SequenceEqual(new[] { cancelCard.InstanceId }, Ids(cancelable.State.Player.DiscardPile));
        Assert.NotEqual(picked.StateHash(), cancelable.StateHash());
    }

    public static void Test_Choice_PromptStatesRejectPlaysPotionsEndTurnAndStrayIndices()
    {
        var sim = NewSim(137UL);
        Equip(sim.State.Player, 0, new AutomaticTestPotion());
        var card = AddToHand(sim, new HandDiscardCard("CHOICE_TEST_DISCARD_ANY", 0, AnyNumber));
        sim.Step(PlayOf(sim, card));
        ulong hash = sim.StateHash();
        var illegal = new[]
        {
            GameAction.EndTurn, GameAction.Play(0), GameAction.Play(0, 0), GameAction.UsePotion(0), GameAction.DiscardPotion(0),
            GameAction.Pick(-1), GameAction.Pick(5), new GameAction(ActionKind.ChoosePick, 1, 0), GameAction.Cancel,
            new GameAction(ActionKind.ChooseConfirm, 1, GameAction.NoIndex), new GameAction((ActionKind)250, 0, 0)
        };
        foreach (var action in illegal)
        {
            Assert.False(sim.IsLegal(action), $"{action} at a prompt");
            Assert.Throws<InvalidOperationException>(() => sim.Step(action), $"{action} at a prompt");
            Assert.Equal(hash, sim.StateHash(), $"rejecting {action} left the prompt unchanged");
        }
        Assert.SequenceEqual(Enumerable.Range(0, 5).Select(GameAction.Pick).Append(GameAction.Confirm), sim.LegalActions());

        var plain = NewSim(137UL);
        foreach (var answer in new[] { GameAction.Pick(0), GameAction.Confirm, GameAction.Cancel })
        {
            Assert.False(plain.IsLegal(answer), $"{answer} without a prompt");
            Assert.Throws<InvalidOperationException>(() => plain.Step(answer));
        }
    }

    public static void Test_Choice_PromptStateLegalActionClosure()
    {
        var makers = new Func<CardDef>[]
        {
            () => new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1),
            () => new HandDiscardCard("CHOICE_TEST_DISCARD_TWO", 2, 2),
            () => new HandDiscardCard("CHOICE_TEST_DISCARD_ANY", 0, AnyNumber),
            () => new HandDiscardCard("CHOICE_TEST_DISCARD_CANCEL", 1, 1, cancelable: true),
            () => new GenerateChooseCard("CHOICE_TEST_CHOOSE_THREE", 3, canSkip: true),
            () => new GenerateChooseCard("CHOICE_TEST_CHOOSE_TWO", 2, canSkip: false),
            () => new HandUpgradeCard("CHOICE_TEST_UPGRADE"),
            () => new PileExhaustCard("CHOICE_TEST_DRAW_EXHAUST", PileType.Draw, 2),
            () => new GridCard("CHOICE_TEST_GRID", 3, 1, 2),
            () => new UnorderedDiscardCard("CHOICE_TEST_UNORDERED"),
            () => new TwoPromptCard("CHOICE_TEST_TWO_PROMPTS"),
        };
        int prompts = 0;
        for (int m = 0; m < makers.Length; m++)
        {
            for (int choice = 0; choice < 3; choice++)
            {
                var sim = NewSim(139UL + (ulong)m);
                var card = AddToHand(sim, makers[m]());
                sim.Step(PlayOf(sim, card));
                Assert.True(sim.PendingChoice != null, $"{card.Id} opens a prompt");
                for (int guard = 0; sim.PendingChoice != null; guard++)
                {
                    Assert.True(guard < 50, $"{card.Id}: the prompt never completed");
                    prompts++;
                    var prompt = sim.PendingChoice!;
                    var listed = sim.LegalActions().ToList();
                    Assert.True(listed.Count > 0, $"{card.Id}: an exposed prompt has a legal answer");
                    Assert.Equal(listed.Count, listed.Distinct().Count(), $"{card.Id}: no action listed twice");
                    ulong hash = sim.StateHash();
                    foreach (var action in listed)
                    {
                        Assert.True(sim.IsLegal(action), $"{card.Id}: listed {action} is legal");
                        var branch = sim.Clone();
                        branch.Step(action);
                    }
                    int n = prompt.Options.Count;
                    var candidates = Enumerable.Range(-1, n + 2).Select(GameAction.Pick)
                        .Concat(new[]
                        {
                            GameAction.Confirm, GameAction.Cancel, new GameAction(ActionKind.ChooseConfirm, 0, GameAction.NoIndex),
                            new GameAction(ActionKind.ChooseCancel, GameAction.NoIndex, 0), new GameAction(ActionKind.ChoosePick, 0, 0),
                            GameAction.EndTurn, GameAction.Play(0), GameAction.UsePotion(0), GameAction.DiscardPotion(0)
                        });
                    foreach (var candidate in candidates)
                    {
                        if (listed.Contains(candidate))
                        {
                            continue;
                        }
                        if (sim.IsLegal(candidate))
                        {
                            Assert.Equal(ActionKind.ChoosePick, candidate.Kind, $"{card.Id}: {candidate} is legal but unlisted");
                            Assert.False(prompt.Ordered, $"{card.Id}: only unordered prompts leave legal picks unlisted");
                            continue;
                        }
                        Assert.Throws<InvalidOperationException>(() => sim.Step(candidate), $"{card.Id}: illegal {candidate}");
                        Assert.Equal(hash, sim.StateHash(), $"{card.Id}: illegal {candidate} changed the state");
                    }
                    sim.Step(listed[(choice + guard) % listed.Count]);
                }
                Assert.True(sim.LegalActions().Contains(GameAction.EndTurn), $"{card.Id}: back at a decision point");
            }
        }
        Assert.True(prompts >= 33, $"only {prompts} prompt states were walked");
    }

    public static void Test_Choice_TapedAndFastPathsReachTheSameHash()
    {
        var start = NewSim(149UL);
        var card = AddToHand(start, new TwoPromptCard("CHOICE_TEST_TWO_PROMPTS"));
        var play = PlayOf(start, card);

        var taped = start.Clone();
        taped.Step(play);
        Assert.Equal(0, taped.PendingTape.Count);
        taped.Step(GameAction.Pick(2));
        Assert.NotNull(taped.PendingChoice, "the second prompt follows the first in the same play");
        Assert.Equal(1, taped.PendingTape.Count);
        Assert.SequenceEqual(new[] { 2 }, taped.PendingTape[0].Picks);
        taped.Step(GameAction.Pick(0));
        Assert.Null(taped.PendingChoice);

        var again = start.Clone();
        again.Step(play);
        again.Step(GameAction.Pick(2));
        again.Step(GameAction.Pick(0));
        Assert.Equal(taped.StateHash(), again.StateHash(), "re-execution is deterministic");

        var fast = start.Clone();
        var answerer = ScriptedAnswerer.Of(new[] { 2 }, new[] { 0 });
        fast.Step(play, answerer);
        Assert.Null(fast.PendingChoice);
        Assert.Equal(2, answerer.Prompts.Count, "the fast path answers both prompts inline");
        Assert.Equal(taped.StateHash(), fast.StateHash(), "the taped path and the fast path with the same answers reach the same state");

        var mixed = start.Clone();
        mixed.Step(play);
        mixed.Step(GameAction.Pick(2), ScriptedAnswerer.Of(new[] { 0 }));
        Assert.Null(mixed.PendingChoice);
        Assert.Equal(taped.StateHash(), mixed.StateHash(), "a fast step from a prompt replays the tape, then answers inline");

        var other = start.Clone();
        other.Step(play, ScriptedAnswerer.Of(new[] { 2 }, new[] { 1 }));
        Assert.NotEqual(taped.StateHash(), other.StateHash());

        Assert.Throws<InvalidOperationException>(() => start.Clone().Step(play, ScriptedAnswerer.Of(new[] { 9 })), "an out-of-range answer is rejected");
        Assert.Throws<InvalidOperationException>(() => start.Clone().Step(play, ScriptedAnswerer.Of(new[] { 1, 2 })), "two picks for a one-pick prompt are rejected");
    }

    public static void Test_Choice_CloneAtAPromptIsIndependent()
    {
        var sim = NewSim(151UL);
        var card = AddToHand(sim, new HandDiscardCard("CHOICE_TEST_DISCARD_TWO", 2, 2));
        sim.Step(PlayOf(sim, card));
        sim.Step(GameAction.Pick(3));
        ulong hash = sim.StateHash();
        var bytes = Bytes(sim);
        var clone = sim.Clone();
        Assert.Equal(hash, clone.StateHash());
        Assert.True(bytes.AsSpan().SequenceEqual(Bytes(clone)));
        Assert.True(ChoiceTape.SameShape(sim.PendingChoice!, clone.PendingChoice!));
        Assert.False(ReferenceEquals(sim.PendingChoice, clone.PendingChoice), "the clone carries its own prompt copy");
        Assert.SequenceEqual(sim.PendingPicks, clone.PendingPicks);
        Assert.SequenceEqual(sim.LegalActions().ToList(), clone.LegalActions().ToList());
        Assert.False(ReferenceEquals(sim.State, clone.State), "the observation is cloned");
        Assert.Equal(StateHasher.Hash(sim.State), StateHasher.Hash(clone.State));
        var twin = sim.Clone();

        clone.State.Player.Gold += 7;
        clone.Step(GameAction.Pick(0));
        Assert.Null(clone.PendingChoice);
        clone.State.Player.Gold += 5;
        Assert.Equal(hash, sim.StateHash(), "stepping and mutating the clone left the source alone");
        Assert.True(bytes.AsSpan().SequenceEqual(Bytes(sim)));
        Assert.SequenceEqual(new[] { 3 }, sim.PendingPicks);
        Assert.Equal(hash, twin.StateHash());

        sim.Step(GameAction.Pick(1));
        twin.Step(GameAction.Pick(1));
        Assert.Equal(sim.StateHash(), twin.StateHash(), "two clones that take the same answer agree");
        Assert.NotEqual(sim.StateHash(), clone.StateHash());
        Assert.Equal(StateHasher.Hash(sim.State), sim.StateHash(), "a completed state hashes as a plain state");
    }

    public static void Test_Choice_PendingHashIsTheTupleNotTheObservation()
    {
        var before = NewSim(153UL);
        var card = AddToHand(before, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        var play = PlayOf(before, card);
        var s0 = before.State.DeepClone();
        before.Step(play);
        Assert.Equal(PendingHash(s0, play, Array.Empty<ChoiceAnswer>(), Array.Empty<int>()), before.StateHash(), "H(S0 || action || tape || picks)");
        Assert.NotEqual(StateHasher.Hash(before.State), before.StateHash(), "the observation alone is not the hash");
        Assert.NotEqual(StateHasher.Hash(s0), before.StateHash());
    }

    public static void Test_Choice_BeginAndGameActionTuplesHashApart()
    {
        var basis = NewSim(155UL).State;
        var tape = new[] { new ChoiceAnswer(new[] { 1 }, false) };
        var picks = new[] { 0 };
        ulong begin = PendingHash(basis, null, tape, picks);
        var actions = new[]
        {
            GameAction.EndTurn, GameAction.Play(0), GameAction.Play(0, 0), GameAction.UsePotion(0), GameAction.DiscardPotion(0),
            GameAction.Pick(0), GameAction.Confirm, GameAction.Cancel, new GameAction(ActionKind.PlayCard, 0, 0)
        };
        foreach (var action in actions)
        {
            Assert.NotEqual(begin, PendingHash(basis, action, tape, picks), $"Begin and {action}");
        }
        Assert.Equal(PendingHash(basis, GameAction.EndTurn, tape, picks), PendingHash(basis.DeepClone(), GameAction.EndTurn, tape, picks));
        Assert.Equal(PendingBytes(basis, GameAction.EndTurn, tape, picks).Length - 9, PendingBytes(basis, null, tape, picks).Length, "Begin writes the presence byte alone");
        Assert.NotEqual(begin, PendingHash(basis, null, Array.Empty<ChoiceAnswer>(), picks), "the tape enters the hash");
        Assert.NotEqual(begin, PendingHash(basis, null, tape, Array.Empty<int>()), "the picks enter the hash");
        Assert.NotEqual(PendingHash(basis, null, new[] { new ChoiceAnswer(Array.Empty<int>(), true) }, picks), PendingHash(basis, null, new[] { new ChoiceAnswer(Array.Empty<int>(), false) }, picks), "cancelled and empty answers differ");
        Assert.NotEqual(PendingHash(basis, null, new[] { new ChoiceAnswer(new[] { 0, 1 }, false) }, picks), PendingHash(basis, null, new[] { new ChoiceAnswer(new[] { 1, 0 }, false) }, picks), "answer order enters the hash");
        Assert.NotEqual(StateHasher.Hash(basis), begin);
    }

    public static void Test_Choice_CreateStopsAtATurnOnePromptThroughBegin()
    {
        Action<CombatState> populate = cs =>
        {
            Spawn(new ChoiceDummy("CHOICE_DUMMY", 60))(cs);
            PowerCmd.Apply<TurnStartDiscardPower>(cs, cs.Player, 1m, cs.Player, null);
        };
        var sim = CombatSim.Create(157UL, Repeat(Defend, 10), TestEncounterId, 0, populate);
        Assert.True(sim.HasPendingChoice, "the AfterPlayerTurnStart prompt of turn 1 stops Create");
        Assert.True(sim.PendingFromBegin);
        Assert.Null(sim.PendingAction);
        var prompt = sim.PendingChoice!;
        Assert.Equal(TurnStartDiscardPower.SiteId, prompt.SiteId);
        Assert.Equal(TurnStartDiscardPower.PowerId, prompt.SourceId);
        Assert.Equal(ChoiceSource.NoInstance, prompt.SourceInstanceId);
        Assert.Equal(5, prompt.Options.Count);
        Assert.Equal(0, prompt.MinSelect);
        Assert.Equal(5, prompt.MaxSelect);
        Assert.True(prompt.RequireConfirm);
        Assert.False(sim.IsTerminal);
        Assert.Equal(1, sim.State.Player.TurnNumber);
        Assert.Equal(5, sim.State.Player.Hand.Count, "the opening hand is drawn before AfterPlayerTurnStart");
        Assert.SequenceEqual(Enumerable.Range(0, 5).Select(GameAction.Pick).Append(GameAction.Confirm), sim.LegalActions());

        var setUp = CombatLoop.SetUpCombat(157UL, Repeat(Defend, 10), TestEncounterId, 0, populate);
        Assert.Equal(PendingHash(setUp, null, Array.Empty<ChoiceAnswer>(), Array.Empty<int>()), sim.StateHash(), "S0 of Begin is the post-SetUpCombat state and the action is absent");

        var hand = sim.State.Player.Hand.Cards.ToList();
        var replay = CombatSim.Create(157UL, Repeat(Defend, 10), TestEncounterId, 0, populate);
        Assert.Equal(sim.StateHash(), replay.StateHash());
        sim.Step(GameAction.Pick(3));
        sim.Step(GameAction.Pick(1));
        sim.Step(GameAction.Confirm);
        Assert.Null(sim.PendingChoice);
        Assert.SequenceEqual(new[] { hand[3].InstanceId, hand[1].InstanceId }, Ids(sim.State.Player.DiscardPile), "discarded in pick order");
        Assert.Equal(3, sim.State.Player.Hand.Count);
        Assert.Equal(PlayerTurnPhase.Play, sim.State.Player.Phase);
        Assert.True(sim.LegalActions().Contains(GameAction.EndTurn));
        replay.Step(GameAction.Pick(3), ScriptedAnswerer.Of());
        replay.Step(GameAction.Pick(1));
        replay.Step(GameAction.Confirm, ScriptedAnswerer.Of());
        Assert.Equal(sim.StateHash(), replay.StateHash());
    }

    public static void Test_Choice_TurnStartPromptReRunsEndTurnFromS0()
    {
        var sim = NewSim(159UL);
        PowerCmd.Apply<TurnStartDiscardPower>(sim.State, sim.State.Player, 1m, sim.State.Player, null);
        var s0 = sim.State.DeepClone();
        sim.Step(GameAction.EndTurn);
        var prompt = Assert.NotNull(sim.PendingChoice, "the next turn's AfterPlayerTurnStart prompt");
        Assert.Equal(GameAction.EndTurn, sim.PendingAction!.Value);
        Assert.Equal(PendingHash(s0, GameAction.EndTurn, Array.Empty<ChoiceAnswer>(), Array.Empty<int>()), sim.StateHash());
        Assert.Equal(CombatSide.Player, sim.State.CurrentSide);
        Assert.Equal(2, sim.State.RoundNumber);
        Assert.Equal(5, prompt.Options.Count);
        sim.Step(GameAction.Pick(4));
        sim.Step(GameAction.Confirm);
        Assert.Null(sim.PendingChoice);
        Assert.Equal(2, sim.State.RoundNumber);
        Assert.Equal(4, sim.State.Player.Hand.Count);
        Assert.Equal(PlayerTurnPhase.Play, sim.State.Player.Phase);

        sim.Step(GameAction.EndTurn);
        Assert.NotNull(sim.PendingChoice, "the power prompts every turn");
        Assert.Equal(3, sim.State.RoundNumber);
        sim.Step(GameAction.Confirm);
        Assert.Null(sim.PendingChoice);
        Assert.Equal(5, sim.State.Player.Hand.Count);
    }

    public static void Test_Choice_EnemyTurnPromptIsExposedWithTheEnemySideCurrent()
    {
        var sim = CombatSim.Create(161UL, Repeat(Defend, 10), TestEncounterId, 0, Spawn(new ChoiceDemon("CHOICE_TEST_DEMON", 80)));
        int demonId = sim.State.Monsters[0].CombatId;
        sim.Step(GameAction.EndTurn);
        var prompt = Assert.NotNull(sim.PendingChoice, "the demon's move asks the player to choose");
        Assert.Equal(CombatSide.Enemy, sim.State.CurrentSide);
        Assert.Equal("CHOICE_TEST_DEMON:ChooseCurse", prompt.SiteId);
        Assert.Equal(demonId, prompt.SourceInstanceId);
        Assert.Equal(ChoicePromptKind.ChooseCard, prompt.Kind);
        Assert.SequenceEqual(new[] { GameAction.Pick(0), GameAction.Pick(1) }, sim.LegalActions());
        int picked = prompt.Options[1].InstanceId;
        sim.Step(GameAction.Pick(1));
        Assert.Null(sim.PendingChoice);
        Assert.Equal(CombatSide.Player, sim.State.CurrentSide, "the answer re-runs EndTurn through the rest of the enemy turn and the next turn start");
        Assert.Equal(2, sim.State.RoundNumber);
        Assert.True(sim.State.Player.AllPiles.SelectMany(p => p.Cards).Any(c => c.InstanceId == picked), "the chosen card was added during the enemy turn");
        Assert.False(sim.State.Player.AllPiles.SelectMany(p => p.Cards).Any(c => c.InstanceId == prompt.Options[0].InstanceId));
    }

    public static void Test_Choice_TurnSetupFirstPauseStepsRunOnceBeforeTheAnswer()
    {
        var sim = NewSim(163UL);
        var probe = Assert.NotNull(PowerCmd.Apply<ChoiceProbePower>(sim.State, sim.State.Player, 1m, sim.State.Player, null));
        int gold = sim.State.Player.Gold;
        var card = AddToHand(sim, new TurnSetupProbeCard("CHOICE_TEST_TURN_SETUP"));
        sim.Step(PlayOf(sim, card));
        Assert.NotNull(sim.PendingChoice);
        var observed = sim.State.Player.GetPower<ChoiceProbePower>()!;
        Assert.Equal(1, observed.Fired, "the first pause ran the steps before the prompt is exposed");
        Assert.Equal(gold + 1, sim.State.Player.Gold);
        Assert.Equal(0, observed.DepthInSteps, "the steps run with the context stack reset to the turn-loop root");
        Assert.Equal((int)ChoiceContextKind.Inline, observed.KindInSteps);
        Assert.Equal(-1, observed.GoldAtAnswer);

        sim.Step(GameAction.Pick(0));
        Assert.NotNull(sim.PendingChoice, "a second selection in the same context");
        var mid = sim.State.Player.GetPower<ChoiceProbePower>()!;
        Assert.Equal(1, mid.Fired, "the second pause does not run the steps again");
        Assert.Equal(gold + 1, mid.GoldAtAnswer, "the steps ran before the answer was applied");
        Assert.Equal(1, mid.DepthAfterAnswer, "the turn-setup frame is restored after its steps");
        Assert.True(mid.DoneAfterAnswer);
        sim.Step(GameAction.Pick(0));
        Assert.Null(sim.PendingChoice);
        var done = sim.State.Player.GetPower<ChoiceProbePower>()!;
        Assert.Equal(1, done.Fired, "exactly once per execution");
        Assert.Equal(gold + 1, sim.State.Player.Gold);
        Assert.Equal(0, sim.State.Choice.ContextDepth);
        Assert.Equal(3, sim.State.Player.DiscardPile.Count, "two discards plus the played card");
        Assert.False(ReferenceEquals(probe, done), "the re-run state owns its power");
    }

    public static void Test_Choice_TurnSetupStepsThatEndTheCombatUnwindToTheTerminalState()
    {
        var sim = NewSim(165UL);
        var card = AddToHand(sim, new UnwindProbeCard("CHOICE_TEST_UNWIND"));
        sim.Step(PlayOf(sim, card));
        Assert.Null(sim.PendingChoice, "the combat ended in the deferred steps: no prompt is exposed");
        Assert.True(sim.IsTerminal);
        Assert.Equal(CombatOutcome.Victory, sim.Outcome);
        Assert.Equal(5, sim.State.Player.Hand.Count, "the rest of the play was unwound: nothing was discarded");
        Assert.Equal(0, sim.LegalActions().Count);
        Assert.Equal(0, sim.State.Choice.ContextDepth);

        var fast = NewSim(165UL);
        var fastCard = AddToHand(fast, new UnwindProbeCard("CHOICE_TEST_UNWIND"));
        fast.Step(PlayOf(fast, fastCard), ScriptedAnswerer.Of());
        Assert.Equal(sim.StateHash(), fast.StateHash());

        var cs = NewSim(167UL).State;
        using (cs.Choice.PushContext(ChoiceContextKind.TurnSetup, () => cs.IsInProgress = false))
        {
            var ex = Assert.Throws<ChoiceUnwindException>(() => cs.Choice.SignalPlayerChoiceBegun(cs, "CHOICE_TEST:Probe"), "a pause whose steps end the combat unwinds");
            Assert.Equal("CHOICE_TEST:Probe", ex.SiteId);
            Assert.Equal(1, cs.Choice.ContextDepth, "the turn-setup frame is restored before the unwind");
        }
        var over = NewSim(167UL).State;
        over.IsInProgress = false;
        int ran = 0;
        using (over.Choice.PushContext(ChoiceContextKind.TurnSetup, () => ran++))
        {
            over.Choice.SignalPlayerChoiceBegun(over, "CHOICE_TEST:Probe");
        }
        Assert.Equal(1, ran, "a combat that was not in progress before the steps does not unwind");
    }

    public static void Test_Choice_PerListenerPromptRunsInlineAndRecordsTheFlag()
    {
        var sim = NewSim(169UL);
        var card = AddToHand(sim, new PerListenerProbeCard("CHOICE_TEST_PER_LISTENER", 2));
        sim.Step(PlayOf(sim, card));
        Assert.NotNull(sim.PendingChoice, "the prompt is exposed inline, not deferred");
        var entries = sim.State.Run.Fidelity.Entries;
        Assert.Equal(1, entries.Count);
        Assert.Equal(FidelityFlag.DeferredListenerPromptInline, entries[0].Flag);
        Assert.Equal("CHOICE_TEST_PER_LISTENER:OnPlay", entries[0].Site);
        sim.Step(GameAction.Pick(0));
        Assert.NotNull(sim.PendingChoice);
        sim.Step(GameAction.Pick(0));
        Assert.Null(sim.PendingChoice);
        Assert.Equal(1, sim.State.Run.Fidelity.Count, "one deferral per listener context, recorded once per execution");
        Assert.True(sim.State.Run.Fidelity.Contains(FidelityFlag.DeferredListenerPromptInline));

        var selected = NewSim(169UL);
        var drawn = Ids(selected.State.Player.DrawPile);
        var selectorCard = AddToHand(selected, new SelectorProbeCard("CHOICE_TEST_SELECTOR"));
        var expected = CardSelectCmd.SortByRarityThenId(selected.State.Player.DrawPile.Cards).Take(2).Select(c => c.InstanceId).ToArray();
        selected.Step(PlayOf(selected, selectorCard));
        Assert.Null(selected.PendingChoice, "VakuuCardSelector answers: no prompt");
        Assert.Equal(0, selected.State.Run.Fidelity.Count, "the selector suppressed the pause, so nothing was deferred");
        Assert.SequenceEqual(expected, Ids(selected.State.Player.ExhaustPile), "Vakuu takes the first MaxSelect options of the sorted draw pile");
        Assert.Equal(0, selected.State.Choice.Selectors.Count, "the scope popped the selector");
        Assert.Equal(5, drawn.Length);
    }

    public static void Test_Choice_ThrowingContextRejectsAPromptAndRestoresTheState()
    {
        var sim = NewSim(171UL);
        var card = AddToHand(sim, new ThrowingProbeCard("CHOICE_TEST_THROWING"));
        ulong hash = sim.StateHash();
        var bytes = Bytes(sim);
        var ex = Assert.Throws<InvalidOperationException>(() => sim.Step(PlayOf(sim, card)));
        Assert.True(ex.Message.Contains("CHOICE_TEST_THROWING:OnPlay", StringComparison.Ordinal), ex.Message);
        Assert.Null(sim.PendingChoice);
        Assert.Equal(hash, sim.StateHash(), "a failed step leaves S0");
        Assert.True(bytes.AsSpan().SequenceEqual(Bytes(sim)));
        Assert.True(sim.State.Player.Hand.Cards.Any(c => c.InstanceId == card.InstanceId), "the card is back in the hand");
        Assert.Equal(6, sim.State.Player.Hand.Count);
        Assert.True(sim.LegalActions().Contains(GameAction.EndTurn));
    }

    public static void Test_Choice_SelectorStackAnswersFirstAndClonesStatefulSelectors()
    {
        var cs = NewSim(173UL).State;
        var runtime = cs.Choice;
        var prompt = new ChoicePrompt(ChoicePromptKind.HandSelect, "CHOICE_TEST:Probe", "CHOICE_TEST", 0, PileType.Hand,
            new[] { new ChoiceOption(1, "A", 0), new ChoiceOption(2, "B", 0), new ChoiceOption(3, "C", 0) }, 1, 1, false, false, false, true, ChoiceResultType.CombatCard);
        Assert.Throws<ChoicePendingException>(() => runtime.Ask(prompt), "no selector and no answerer: the prompt is pending");
        runtime.Answerer = ScriptedAnswerer.Of(new[] { 1 });
        Assert.SequenceEqual(new[] { 1 }, runtime.Ask(prompt).Picks);
        runtime.PushSelector(new VakuuCardSelector());
        runtime.PushSelector(new FixedSelector(2));
        Assert.SequenceEqual(new[] { 2 }, runtime.Ask(prompt).Picks, "the top selector answers before the answerer");
        runtime.PopSelector();
        Assert.SequenceEqual(new[] { 0 }, runtime.Ask(prompt).Picks, "Vakuu takes the first MaxSelect options");
        runtime.PopSelector();
        runtime.Answerer = null;

        Assert.Throws<ArgumentException>(() => runtime.PushSelector(ScriptedAnswerer.Of()), "a selector needs a stable id");
        Assert.Throws<ArgumentException>(() => runtime.PushSelector(new UncloneableSelector()), "a selector with canonical state needs the clone protocol");
        var vakuu = new VakuuCardSelector();
        runtime.PushSelector(vakuu);
        Assert.False(runtime.PopSelector(new VakuuCardSelector()), "a guarded pop removes only the same selector");
        Assert.True(runtime.PopSelector(vakuu));
        Assert.False(runtime.PopSelector(vakuu), "a second guarded pop does nothing");
        var scope = runtime.PushSelectorScope(new VakuuCardSelector());
        runtime.PushSelector(new FixedSelector(0));
        scope.Dispose();
        Assert.Equal(2, runtime.Selectors.Count, "a scope pops only when its selector is on top");
        runtime.PopSelector();
        scope.Dispose();
        Assert.Equal(1, runtime.Selectors.Count, "a disposed scope pops at most once");
        runtime.PopSelector();

        var sim = NewSim(173UL);
        var counting = new CountingSelector();
        sim.State.Choice.PushSelector(counting);
        sim.State.Choice.PushSelector(counting);
        var card = AddToHand(sim, new HandDiscardCard("CHOICE_TEST_DISCARD_ONE", 1, 1));
        var play = PlayOf(sim, card);
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        var cloned = (CountingSelector)clone.State.Choice.Selectors[0];
        Assert.False(ReferenceEquals(counting, cloned), "a stateful selector is cloned, never shared");
        Assert.True(ReferenceEquals(clone.State.Choice.Selectors[0], clone.State.Choice.Selectors[1]), "aliasing inside one state is preserved");
        var stateless = new VakuuCardSelector();
        var shared = NewSim(173UL);
        shared.State.Choice.PushSelector(stateless);
        Assert.True(ReferenceEquals(stateless, shared.Clone().State.Choice.Selectors[0]), "a stateless selector may be shared");

        ulong before = sim.StateHash();
        clone.Step(play);
        Assert.Null(clone.PendingChoice, "the selector answered");
        Assert.Equal(1, cloned.Calls);
        Assert.Equal(0, counting.Calls, "the source's selector did not move");
        Assert.Equal(before, sim.StateHash());
        sim.Step(play);
        Assert.Equal(1, ((CountingSelector)sim.State.Choice.Selectors[0]).Calls);
        Assert.Equal(sim.StateHash(), clone.StateHash(), "the same history on both sides");
        var copy = sim.Clone();
        ((CountingSelector)copy.State.Choice.Selectors[0]).Bump();
        Assert.NotEqual(sim.StateHash(), copy.StateHash(), "selector state is hashed");
    }

    public static void Test_Choice_ContextStackRules()
    {
        var cs = NewSim(175UL).State;
        var runtime = cs.Choice;
        Assert.Equal(ChoiceContextKind.Inline, runtime.CurrentContextKind, "an empty stack behaves as an inline context");
        Assert.Throws<ArgumentException>(() => runtime.PushContext(ChoiceContextKind.Inline, () => { }));
        Assert.Throws<ArgumentException>(() => runtime.PushContext(ChoiceContextKind.PerListener, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.PushContext((ChoiceContextKind)9));
        var outer = runtime.PushContext(ChoiceContextKind.TurnSetup, () => { });
        var inner = runtime.PushContext(ChoiceContextKind.Inline);
        Assert.Equal(ChoiceContextKind.Inline, runtime.CurrentContextKind);
        runtime.SignalPlayerChoiceBegun(cs, "CHOICE_TEST:Probe");
        Assert.False(outer.FirstPauseDone, "a turn-setup context pauses only while it is on top");
        Assert.Throws<InvalidOperationException>(() => outer.Dispose(), "contexts pop in LIFO order");
        inner.Dispose();
        runtime.SignalPlayerChoiceBegun(cs, "CHOICE_TEST:Probe");
        Assert.True(outer.FirstPauseDone);
        outer.Dispose();
        Assert.Equal(0, runtime.ContextDepth);
        default(ContextScope).Dispose();
        Assert.Equal(0, cs.DeepClone().Choice.ContextDepth);
        using (runtime.PushContext(ChoiceContextKind.Throwing))
        {
            var copy = cs.DeepClone();
            Assert.Equal(0, copy.Choice.ContextDepth, "the context stack is transient: not cloned");
            Assert.Equal(StateHasher.Hash(copy), StateHasher.Hash(cs), "and not hashed");
        }
    }

    public static void Test_Choice_TapeInvariantComparesPromptsElementWise()
    {
        var a = new ChoicePrompt(ChoicePromptKind.PileSelect, "X:OnPlay", "X", 3, PileType.Draw,
            new[] { new ChoiceOption(5, "STRIKE_IRONCLAD", 0), new ChoiceOption(6, "DEFEND_IRONCLAD", 1) }, 1, 1, false, false, false, true, ChoiceResultType.CombatCard);
        var b = ChoiceTape.Copy(a);
        Assert.False(a == b, "record equality compares the option lists by reference");
        Assert.True(ChoiceTape.SameShape(a, b));
        Assert.True(ChoiceTape.SameShape(a, b with { MinSelect = 0, SourceInstanceId = 9 }), "the invariant compares kind, site and option ids only");
        Assert.False(ChoiceTape.SameShape(a, b with { Kind = ChoicePromptKind.HandSelect }));
        Assert.False(ChoiceTape.SameShape(a, b with { SiteId = "Y:OnPlay" }));
        Assert.False(ChoiceTape.SameShape(a, b with { Options = new[] { new ChoiceOption(5, "STRIKE_IRONCLAD", 0), new ChoiceOption(7, "DEFEND_IRONCLAD", 1) } }));
        Assert.False(ChoiceTape.SameShape(a, b with { Options = new[] { new ChoiceOption(5, "STRIKE_IRONCLAD", 0), new ChoiceOption(6, "BASH", 1) } }));
        Assert.False(ChoiceTape.SameShape(a, b with { Options = new[] { new ChoiceOption(5, "STRIKE_IRONCLAD", 0) } }));

        var tape = new ChoiceTapeAnswerer(new[] { new ChoiceAnswer(new[] { 1 }, false) }, new[] { a });
        Assert.SequenceEqual(new[] { 1 }, tape.Answer(b).Picks);
        Assert.True(tape.IsExhausted);
        Assert.Throws<ChoicePendingException>(() => tape.Answer(b), "an exhausted tape exposes the prompt");
        var diverging = new ChoiceTapeAnswerer(new[] { new ChoiceAnswer(new[] { 1 }, false) }, new[] { a });
        Assert.Throws<InvalidOperationException>(() => diverging.Answer(b with { SiteId = "Y:OnPlay" }), "a re-run that asks another prompt is a divergence");
        var fallback = new ChoiceTapeAnswerer(Array.Empty<ChoiceAnswer>(), Array.Empty<ChoicePrompt>(), ScriptedAnswerer.Of(new[] { 0 }));
        Assert.SequenceEqual(new[] { 0 }, fallback.Answer(a).Picks, "after the tape the fallback answers");
    }

    public static void Test_Choice_DiscardPotionListingOrderAndGates()
    {
        var sim = NewSim(177UL);
        var player = sim.State.Player;
        Equip(player, 0, new AutomaticTestPotion());
        Equip(player, 2, new AutomaticTestPotion());
        var legal = sim.LegalActions().ToList();
        int firstPotion = legal.FindIndex(a => a.Kind is ActionKind.UsePotion or ActionKind.DiscardPotion);
        Assert.True(legal.Take(firstPotion).All(a => a.Kind == ActionKind.PlayCard), "plays first");
        Assert.SequenceEqual(new[] { GameAction.DiscardPotion(0), GameAction.DiscardPotion(2), GameAction.EndTurn }, legal.Skip(firstPotion),
            "an Automatic potion can only be discarded; discards by slot ascending, then EndTurn");
        Assert.False(sim.IsLegal(GameAction.UsePotion(0)), "Automatic potions are never used by the player");
        Assert.False(sim.IsLegal(new GameAction(ActionKind.DiscardPotion, 0, 0)), "a DiscardPotion with a target index is malformed");
        Assert.False(sim.IsLegal(GameAction.DiscardPotion(1)), "empty slot");
        Assert.False(sim.IsLegal(GameAction.DiscardPotion(-1)));
        Assert.False(sim.IsLegal(GameAction.DiscardPotion(3)));
        Assert.False(sim.IsLegal(GameAction.UsePotion(1)));
        Assert.False(sim.IsLegal(GameAction.UsePotion(5)));
        Assert.True(sim.IsLegal(GameAction.DiscardPotion(2)));

        player.CanUseOrRemovePotions = false;
        Assert.False(sim.LegalActions().Any(a => a.Kind is ActionKind.UsePotion or ActionKind.DiscardPotion), "CanUseOrRemovePotions gates both");
        Assert.False(sim.IsLegal(GameAction.DiscardPotion(0)));
        player.CanUseOrRemovePotions = true;
        player.Phase = PlayerTurnPhase.AutoPrePlay;
        Assert.False(sim.LegalActions().Any(a => a.Kind is ActionKind.UsePotion or ActionKind.DiscardPotion), "potion actions run only in the play phase");
        player.Phase = PlayerTurnPhase.Play;
        Assert.True(sim.IsLegal(GameAction.DiscardPotion(0)));
    }

    public static void Test_Choice_PotionTargetIndexResolution()
    {
        var cs = NewSim(179UL).State;
        var self = new SelfTestPotion();
        var enemy = new EnemyTestPotion();
        var area = new AreaTestPotion();
        Assert.True(LegalActionGenerator.TryResolvePotionTarget(cs, self, GameAction.NoIndex, out var t1, out _));
        Assert.True(ReferenceEquals(cs.Player, t1), "NoIndex is the owner for a single-target type");
        Assert.True(LegalActionGenerator.TryResolvePotionTarget(cs, enemy, GameAction.NoIndex, out var t2, out _));
        Assert.True(ReferenceEquals(cs.Player, t2), "AnyEnemy is single-target: NoIndex resolves to the owner, which IsValidTarget then rejects");
        Assert.True(LegalActionGenerator.TryResolvePotionTarget(cs, area, GameAction.NoIndex, out var t3, out _));
        Assert.Null(t3, "NoIndex is no creature for a multi-target type");
        Assert.True(LegalActionGenerator.TryResolvePotionTarget(cs, enemy, 0, out var t4, out _));
        Assert.True(ReferenceEquals(cs.Monsters[0], t4));
        Assert.False(LegalActionGenerator.TryResolvePotionTarget(cs, enemy, 1, out _, out _), "past the monster list");
        Assert.False(LegalActionGenerator.TryResolvePotionTarget(cs, self, LegalActionGenerator.PlayerAllyTargetIndex, out _, out var err), "the player is NoIndex, not Allies[0]");
        Assert.True(err!.Length > 0);
        Assert.False(LegalActionGenerator.TryResolvePotionTarget(cs, self, LegalActionGenerator.FirstPetTargetIndex, out _, out _), "no pet");
        foreach (var type in new[] { TargetType.Self, TargetType.AnyEnemy, TargetType.AnyPlayer, TargetType.AnyAlly, TargetType.TargetedNoCreature })
        {
            Assert.True(LegalActionGenerator.IsSingleTarget(type), type.ToString());
        }
        foreach (var type in new[] { TargetType.None, TargetType.AllEnemies, TargetType.RandomEnemy, TargetType.AllAllies, TargetType.Osty })
        {
            Assert.False(LegalActionGenerator.IsSingleTarget(type), type.ToString());
        }
    }

    public static void Test_Choice_UsePotionListingDelegatesToPotionActions()
    {
        W1Pending.Run(nameof(Test_Choice_UsePotionListingDelegatesToPotionActions), "relics-potions", () =>
        {
            var sim = NewSim(181UL);
            var player = sim.State.Player;
            Equip(player, 0, new EnemyTestPotion());
            Equip(player, 1, new SelfTestPotion());
            Equip(player, 2, new AreaTestPotion());
            var legal = sim.LegalActions().ToList();
            int firstPotion = legal.FindIndex(a => a.Kind is ActionKind.UsePotion or ActionKind.DiscardPotion);
            Assert.SequenceEqual(new[]
            {
                GameAction.UsePotion(0, 0), GameAction.UsePotion(1), GameAction.UsePotion(2),
                GameAction.DiscardPotion(0), GameAction.DiscardPotion(1), GameAction.DiscardPotion(2), GameAction.EndTurn
            }, legal.Skip(firstPotion), "UsePotion by slot then target, then DiscardPotion by slot, then EndTurn; one action per resolved target");
            Assert.False(sim.IsLegal(GameAction.UsePotion(0)), "AnyEnemy with NoIndex resolves to the owner, an invalid target");
            Assert.False(sim.IsLegal(GameAction.UsePotion(1, 0)), "Self potion on a monster");
            Assert.False(sim.IsLegal(GameAction.UsePotion(2, 0)), "a multi-target potion takes no creature");
            Assert.False(sim.IsLegal(GameAction.UsePotion(1, LegalActionGenerator.PlayerAllyTargetIndex)));
            Assert.True(LegalActionGenerator.TryResolve(sim.State, GameAction.UsePotion(0, 0), out var target, out _));
            Assert.True(ReferenceEquals(sim.State.Monsters[0], target));
            Assert.True(LegalActionGenerator.TryResolve(sim.State, GameAction.UsePotion(2), out var none, out _));
            Assert.Null(none);
        });
    }

    private static CombatSim NewSim(ulong seed) => CombatSim.Create(seed, Repeat(Defend, 10), TestEncounterId, 0, Spawn(new ChoiceDummy("CHOICE_DUMMY", 60)));

    private static IReadOnlyList<string> Repeat(string id, int count) => Enumerable.Repeat(id, count).ToList();

    private static Action<CombatState> Spawn(params MonsterDef[] defs) => cs =>
    {
        foreach (var def in defs)
        {
            int hp = def.MaxInitialHp(cs);
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, hp, hp, null, null));
        }
    };

    private static ChoiceSource Source(string modelId) => ChoiceSource.For(modelId, ChoiceSource.NoInstance, "Probe");

    private static CardInstance AddToHand(CombatSim sim, CardDef def)
    {
        var cs = sim.State;
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        cs.Player.Hand.AddInternal(card);
        return card;
    }

    private static List<CardInstance> HandWithout(CombatSim sim, CardInstance card) => sim.State.Player.Hand.Cards.Where(c => !ReferenceEquals(c, card)).ToList();

    private static GameAction PlayOf(CombatSim sim, CardInstance card) => GameAction.Play(sim.State.Player.Hand.IndexOf(card));

    private static void TrimHand(CombatSim sim, int keep)
    {
        var player = sim.State.Player;
        while (player.Hand.Count > keep)
        {
            player.DrawPile.AddInternal(player.Hand.RemoveAtInternal(player.Hand.Count - 1));
        }
    }

    private static List<CardInstance> SetDrawPile(CombatSim sim, params CardDef[] defs) => SetPile(sim.State.Player.DrawPile, sim.State, defs);

    private static List<CardInstance> SetPile(CardPile pile, CombatState cs, params CardDef[] defs)
    {
        pile.ClearInternal();
        var cards = new List<CardInstance>(defs.Length);
        foreach (var def in defs)
        {
            var card = new CardInstance(def, cs.AllocateCardInstanceId());
            card.AttachTo(cs.Player);
            pile.AddInternal(card);
            cards.Add(card);
        }
        return cards;
    }

    private static void Equip(PlayerState player, int slot, PotionModel potion)
    {
        potion.AttachTo(player);
        player.PotionSlots[slot] = potion;
    }

    private static int[] Ids(CardPile pile) => pile.Cards.Select(c => c.InstanceId).ToArray();

    private static byte[] Bytes(CombatSim sim)
    {
        var w = new CanonicalWriter();
        sim.WriteCanonical(w);
        return w.ToArray();
    }

    private static byte[] PendingBytes(CombatState basis, GameAction? action, IReadOnlyList<ChoiceAnswer> tape, IReadOnlyList<int> picks)
    {
        var w = new CanonicalWriter();
        ChoiceTape.WritePending(w, basis, action, tape, picks);
        return w.ToArray();
    }

    private static ulong PendingHash(CombatState basis, GameAction? action, IReadOnlyList<ChoiceAnswer> tape, IReadOnlyList<int> picks)
        => StateHasher.Hash(PendingBytes(basis, action, tape, picks));

    private static List<CardInstance> Discard(CombatState cs, IEnumerable<CardInstance> picked)
    {
        var list = picked.ToList();
        foreach (var card in list)
        {
            CardPileCmd.Discard(cs, card);
        }
        return list;
    }

    private abstract class ChoiceTestCard : CardDef
    {
        protected ChoiceTestCard(string id, CardRarity rarity = CardRarity.Common)
            : base(id, 0, CardType.Skill, rarity, TargetType.None)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play) => Resolve(cs, play.Card);

        protected virtual void Resolve(CombatState cs, CardInstance card)
        {
        }
    }

    private sealed class RarityCard : ChoiceTestCard
    {
        public RarityCard(string id, CardRarity rarity)
            : base(id, rarity)
        {
        }
    }

    private sealed class HandDiscardCard : ChoiceTestCard
    {
        private readonly int _min;
        private readonly int _max;
        private readonly bool _cancelable;
        private readonly bool? _requireConfirm;

        public HandDiscardCard(string id, int min, int max, bool cancelable = false, bool? requireConfirm = null)
            : base(id)
        {
            _min = min;
            _max = max;
            _cancelable = cancelable;
            _requireConfirm = requireConfirm;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, _min, _max) { Cancelable = _cancelable };
            if (_requireConfirm is { } requireConfirm)
            {
                prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, _min, _max) { Cancelable = _cancelable, RequireManualConfirmation = requireConfirm };
            }
            Discard(cs, CardSelectCmd.FromHandForDiscard(cs, cs.Player, prefs, null, ChoiceSource.For(card, OnPlayMethod)));
        }
    }

    private sealed class HandUpgradeCard : ChoiceTestCard
    {
        public HandUpgradeCard(string id)
            : base(id)
        {
        }

        public override int MaxUpgradeLevel => 0;

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var picked = CardSelectCmd.FromHandForUpgrade(cs, cs.Player, ChoiceSource.For(card, OnPlayMethod));
            if (picked != null)
            {
                CardPileCmd.Exhaust(cs, picked);
            }
        }
    }

    private sealed class PileExhaustCard : ChoiceTestCard
    {
        private readonly PileType _pile;
        private readonly int _count;

        public PileExhaustCard(string id, PileType pile, int count)
            : base(id)
        {
            _pile = pile;
            _count = count;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var picked = CardSelectCmd.FromCombatPile(cs, cs.Player.GetPile(_pile), cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, _count), ChoiceSource.For(card, OnPlayMethod)).ToList();
            foreach (var c in picked)
            {
                CardPileCmd.Exhaust(cs, c);
            }
        }
    }

    private sealed class GenerateChooseCard : ChoiceTestCard
    {
        private readonly int _count;
        private readonly bool _canSkip;

        public GenerateChooseCard(string id, int count, bool canSkip)
            : base(id)
        {
            _count = count;
            _canSkip = canSkip;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var def = CardRegistry.Get(Strike);
            var options = new List<CardInstance>(_count);
            for (int i = 0; i < _count; i++)
            {
                options.Add(cs.CreateCard(def, cs.Player));
            }
            var picked = CardSelectCmd.FromChooseACardScreen(cs, options, cs.Player, _canSkip, ChoiceSource.For(card, OnPlayMethod));
            if (picked != null)
            {
                CardPileCmd.AddGeneratedCardToCombat(cs, picked, PileType.Hand);
            }
        }
    }

    private sealed class GridCard : ChoiceTestCard
    {
        private readonly int _count;
        private readonly int _min;
        private readonly int _max;

        public GridCard(string id, int count, int min, int max)
            : base(id)
        {
            _count = count;
            _min = min;
            _max = max;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var def = CardRegistry.Get(Strike);
            var options = new List<CardInstance>(_count);
            for (int i = 0; i < _count; i++)
            {
                options.Add(cs.CreateCard(def, cs.Player));
            }
            var picked = CardSelectCmd.FromSimpleGrid(cs, options, cs.Player, new CardSelectorPrefs("CHOICE_TEST_GRID", _min, _max), ChoiceSource.For(card, OnPlayMethod)).ToList();
            foreach (var c in picked)
            {
                CardPileCmd.AddGeneratedCardToCombat(cs, c, PileType.Hand);
            }
        }
    }

    private sealed class UnorderedDiscardCard : ChoiceTestCard
    {
        private readonly bool _thenPile;

        public UnorderedDiscardCard(string id, bool thenPile = false)
            : base(id)
        {
            _thenPile = thenPile;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var source = ChoiceSource.For(card, OnPlayMethod);
            var choice = cs.Choice;
            var hand = cs.Player.Hand.Cards.ToList();
            if (!choice.HasSelector)
            {
                choice.SignalPlayerChoiceBegun(cs, source.SiteId);
            }
            var options = hand.Select(c => new ChoiceOption(c.InstanceId, c.Id, c.UpgradeLevel)).ToArray();
            var prompt = new ChoicePrompt(ChoicePromptKind.HandSelect, source.SiteId, source.SourceId, source.SourceInstanceId, PileType.Hand, options, 2, 2, false, false, false, false, ChoiceResultType.CombatCard);
            var answer = choice.Ask(prompt);
            Discard(cs, answer.Picks.Select(i => hand[i]));
            if (_thenPile)
            {
                var picked = CardSelectCmd.FromCombatPile(cs, cs.Player.DrawPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1), source).ToList();
                foreach (var c in picked)
                {
                    CardPileCmd.Exhaust(cs, c);
                }
            }
        }
    }

    private sealed class TwoPromptCard : ChoiceTestCard
    {
        public TwoPromptCard(string id)
            : base(id)
        {
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var source = ChoiceSource.For(card, OnPlayMethod);
            Discard(cs, CardSelectCmd.FromHandForDiscard(cs, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, source));
            var picked = CardSelectCmd.FromCombatPile(cs, cs.Player.DrawPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1), source).ToList();
            foreach (var c in picked)
            {
                CardPileCmd.Exhaust(cs, c);
            }
        }
    }

    private sealed class TurnSetupProbeCard : ChoiceTestCard
    {
        public TurnSetupProbeCard(string id)
            : base(id)
        {
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            var player = cs.Player;
            var probe = player.GetPower<ChoiceProbePower>()!;
            var source = ChoiceSource.For(card, OnPlayMethod);
            using (var scope = cs.Choice.PushContext(ChoiceContextKind.TurnSetup, () =>
            {
                probe.Fired++;
                player.Gold += 1;
                probe.DepthInSteps = cs.Choice.ContextDepth;
                probe.KindInSteps = (int)cs.Choice.CurrentContextKind;
            }))
            {
                var first = CardSelectCmd.FromHandForDiscard(cs, player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, source).ToList();
                probe.GoldAtAnswer = player.Gold;
                probe.DepthAfterAnswer = cs.Choice.ContextDepth;
                probe.DoneAfterAnswer = scope.FirstPauseDone;
                Discard(cs, first);
                Discard(cs, CardSelectCmd.FromHandForDiscard(cs, player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, source));
            }
        }
    }

    private sealed class UnwindProbeCard : ChoiceTestCard
    {
        public UnwindProbeCard(string id)
            : base(id)
        {
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            using (cs.Choice.PushContext(ChoiceContextKind.TurnSetup, () =>
            {
                foreach (var monster in cs.Monsters.ToArray())
                {
                    CreatureCmd.Kill(cs, monster);
                }
                CombatLoop.CheckWinCondition(cs);
            }))
            {
                Discard(cs, CardSelectCmd.FromHandForDiscard(cs, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, ChoiceSource.For(card, OnPlayMethod)));
            }
        }
    }

    private sealed class PerListenerProbeCard : ChoiceTestCard
    {
        private readonly int _selections;

        public PerListenerProbeCard(string id, int selections)
            : base(id)
        {
            _selections = selections;
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
            {
                for (int i = 0; i < _selections; i++)
                {
                    Discard(cs, CardSelectCmd.FromHandForDiscard(cs, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, ChoiceSource.For(card, OnPlayMethod)));
                }
            }
        }
    }

    private sealed class ThrowingProbeCard : ChoiceTestCard
    {
        public ThrowingProbeCard(string id)
            : base(id)
        {
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
            {
                Discard(cs, CardSelectCmd.FromHandForDiscard(cs, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, ChoiceSource.For(card, OnPlayMethod)));
            }
        }
    }

    private sealed class SelectorProbeCard : ChoiceTestCard
    {
        public SelectorProbeCard(string id)
            : base(id)
        {
        }

        protected override void Resolve(CombatState cs, CardInstance card)
        {
            using (cs.Choice.PushContext(ChoiceContextKind.PerListener))
            using (cs.Choice.PushSelectorScope(new VakuuCardSelector()))
            {
                var picked = CardSelectCmd.FromCombatPile(cs, cs.Player.DrawPile, cs.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 2), ChoiceSource.For(card, OnPlayMethod)).ToList();
                foreach (var c in picked)
                {
                    CardPileCmd.Exhaust(cs, c);
                }
            }
        }
    }

    private sealed class TurnStartDiscardPower : PowerModel
    {
        public const string PowerId = "CHOICE_TEST_TURN_START_POWER";
        public const string Method = "AfterPlayerTurnStart";
        public const string SiteId = PowerId + ":" + Method;

        public override string Id => PowerId;

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
        {
            if (!ReferenceEquals(player, Owner))
            {
                return;
            }
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 0, AnyNumber);
            Discard(cs, CardSelectCmd.FromHandForDiscard(cs, player, prefs, null, ChoiceSource.For(PowerId, ChoiceSource.NoInstance, Method)));
        }
    }

    private sealed class ChoiceProbePower : PowerModel
    {
        public override string Id => "CHOICE_TEST_PROBE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public int Fired { get; set; }

        public int GoldAtAnswer { get; set; } = -1;

        public int DepthInSteps { get; set; } = -1;

        public int KindInSteps { get; set; } = -1;

        public int DepthAfterAnswer { get; set; } = -1;

        public bool DoneAfterAnswer { get; set; }

        protected override void WriteExtraCanonical(CanonicalWriter w)
        {
            w.WriteInt32(Fired);
            w.WriteInt32(GoldAtAnswer);
            w.WriteInt32(DepthInSteps);
            w.WriteInt32(KindInSteps);
            w.WriteInt32(DepthAfterAnswer);
            w.WriteBool(DoneAfterAnswer);
        }
    }

    private sealed class ChoiceDummy : MonsterDef
    {
        private const string WaitMoveId = "CHOICE_WAIT";
        private readonly int _hp;

        public ChoiceDummy(string id, int hp)
            : base(id)
        {
            _hp = hp;
        }

        public override int MinInitialHp(CombatState cs) => _hp;

        public override int MaxInitialHp(CombatState cs) => _hp;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var wait = new MoveStateDef(WaitMoveId, static (_, _, _) => { }) { FollowUpStateId = WaitMoveId };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
        }
    }

    private sealed class ChoiceDemon : MonsterDef
    {
        private const string MoveId = "CHOICE_DEMON_CHOOSE";
        private readonly int _hp;

        public ChoiceDemon(string id, int hp)
            : base(id)
        {
            _hp = hp;
        }

        public override int MinInitialHp(CombatState cs) => _hp;

        public override int MaxInitialHp(CombatState cs) => _hp;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var move = new MoveStateDef(MoveId, static (c, s, _) => ChooseCurse(c, s)) { FollowUpStateId = MoveId };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { move }, move);
        }

        private static void ChooseCurse(CombatState cs, MonsterState self)
        {
            var player = cs.Player;
            var def = CardRegistry.Get(Strike);
            var options = new[] { cs.CreateCard(def, player), cs.CreateCard(def, player) };
            CardInstance? picked;
            using (cs.Choice.PushContext(ChoiceContextKind.Inline))
            {
                picked = CardSelectCmd.FromChooseACardScreen(cs, options, player, false, ChoiceSource.For(self.MonsterId, self.CombatId, "ChooseCurse"));
            }
            if (picked != null)
            {
                CardPileCmd.AddGeneratedCardToCombat(cs, picked, PileType.Discard);
            }
        }
    }

    private sealed class AutomaticTestPotion : PotionModel
    {
        public override string Id => "CHOICE_TEST_AUTOMATIC_POTION";

        public override PotionRarity Rarity => PotionRarity.Rare;

        public override PotionUsage Usage => PotionUsage.Automatic;

        public override TargetType TargetType => TargetType.Self;
    }

    private sealed class SelfTestPotion : PotionModel
    {
        public override string Id => "CHOICE_TEST_SELF_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.AnyTime;

        public override TargetType TargetType => TargetType.Self;
    }

    private sealed class EnemyTestPotion : PotionModel
    {
        public override string Id => "CHOICE_TEST_ENEMY_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.AnyEnemy;
    }

    private sealed class AreaTestPotion : PotionModel
    {
        public override string Id => "CHOICE_TEST_AREA_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.AllEnemies;
    }

    private sealed class ScriptedAnswerer : IChoiceAnswerer
    {
        private readonly Queue<int[]> _answers;

        private ScriptedAnswerer(IEnumerable<int[]> answers)
        {
            _answers = new Queue<int[]>(answers);
        }

        public List<ChoicePrompt> Prompts { get; } = new();

        public static ScriptedAnswerer Of(params int[][] answers) => new(answers);

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Prompts.Add(prompt);
            if (_answers.Count == 0)
            {
                throw new InvalidOperationException($"No scripted answer left for {prompt.SiteId}.");
            }
            return new ChoiceAnswer(_answers.Dequeue(), false);
        }
    }

    private sealed class FixedSelector : IChoiceSelector
    {
        private readonly int _pick;

        public FixedSelector(int pick)
        {
            _pick = pick;
        }

        public string SelectorId => "CHOICE_TEST_FIXED_SELECTOR";

        public ChoiceAnswer Answer(ChoicePrompt prompt) => new(new[] { _pick }, false);
    }

    private sealed class RecordingSelector : IChoiceSelector
    {
        public List<ChoicePrompt> Prompts { get; } = new();

        public string SelectorId => "CHOICE_TEST_RECORDING_SELECTOR";

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Prompts.Add(prompt);
            return new ChoiceAnswer(new[] { 0 }, false);
        }
    }

    private sealed class CountingSelector : IStatefulChoiceSelector
    {
        public int Calls { get; private set; }

        public string SelectorId => "CHOICE_TEST_COUNTING_SELECTOR";

        public void Bump() => Calls++;

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            int pick = Calls % prompt.Options.Count;
            Calls++;
            return new ChoiceAnswer(new[] { pick }, false);
        }

        public IStatefulChoiceSelector DeepClone() => new CountingSelector { Calls = Calls };

        public void WriteCanonical(CanonicalWriter w) => w.WriteInt32(Calls);
    }

    private sealed class UncloneableSelector : IChoiceSelector, ICanonical
    {
        public int Calls { get; private set; }

        public string SelectorId => "CHOICE_TEST_UNCLONEABLE_SELECTOR";

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Calls++;
            return new ChoiceAnswer(new[] { 0 }, false);
        }

        public void WriteCanonical(CanonicalWriter w) => w.WriteInt32(Calls);
    }
}
