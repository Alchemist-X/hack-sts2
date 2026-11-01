namespace Sts2Sim.Core.Choice;

public static class CardSelectCmd
{
    public const int MaxChooseACardOptions = 3;

    public const string MinSelectClippedSite = "CardSelectCmd:MinSelectClippedToOptions@";

    public const string EmptySelectionWithoutDecisionSite = "CardSelectCmd:ZeroMaxSelectCompletedWithoutDecision@";

    private static readonly Func<CardInstance, bool> AnyCard = static _ => true;

    private static readonly Func<CardInstance, bool> Upgradable = static c => c.IsUpgradable;

    public static CardInstance? FromChooseACardScreen(CombatState cs, IReadOnlyList<CardInstance> cards, PlayerState player, bool canSkip = false, ChoiceSource source = default)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(player);
        RequireSource(source);
        if (cards.Count > MaxChooseACardOptions)
        {
            throw new ArgumentException("Only works with less than 3 cards", nameof(cards));
        }
        if (cards.Count == 0)
        {
            return null;
        }
        var choice = cs.Choice;
        bool selector = choice.HasSelector;
        if (!selector)
        {
            choice.SignalPlayerChoiceBegun(cs, source.SiteId);
        }
        var prompt = new ChoicePrompt(
            ChoicePromptKind.ChooseCard,
            source.SiteId,
            source.SourceId,
            source.SourceInstanceId,
            null,
            Options(cards),
            selector || canSkip ? 0 : 1,
            1,
            false,
            canSkip,
            false,
            true,
            ChoiceResultType.Index);
        var answer = choice.Ask(prompt);
        return answer.Picks.Count > 0 ? cards[answer.Picks[0]] : null;
    }

    public static IEnumerable<CardInstance> FromSimpleGrid(CombatState cs, IReadOnlyList<CardInstance> cardsIn, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source = default)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cardsIn);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(prefs);
        RequireSource(source);
        if (CombatStatus.IsEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var cards = new List<CardInstance>(cardsIn);
        var choice = cs.Choice;
        bool selector = choice.HasSelector;
        if (!selector)
        {
            choice.SignalPlayerChoiceBegun(cs, source.SiteId);
        }
        if (cards.Count == 0)
        {
            return new List<CardInstance>();
        }
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
        {
            return new List<CardInstance>(cards);
        }
        return Select(cs, ChoicePromptKind.SimpleGrid, source, null, cards, prefs, ChoiceResultType.Index, screenClipsMinSelect: false, selector);
    }

    public static IEnumerable<CardInstance> FromCombatPile(CombatState cs, CardPile pile, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source = default)
        => FromCombatPile(cs, pile, player, prefs, AnyCard, source);

    public static IEnumerable<CardInstance> FromCombatPile(CombatState cs, CardPile pile, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source = default)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(pile);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(prefs);
        RequireSource(source);
        if (CombatStatus.IsEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        if (!pile.IsCombatPile)
        {
            throw new InvalidOperationException("Cannot perform on a non combat pile");
        }
        var choice = cs.Choice;
        bool selector = choice.HasSelector;
        if (!selector)
        {
            choice.SignalPlayerChoiceBegun(cs, source.SiteId);
        }
        IReadOnlyList<CardInstance> cards = filter == null ? pile.Cards : Where(pile.Cards, filter);
        int num = cards.Count;
        if (num == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (!prefs.RequireManualConfirmation && num <= prefs.MinSelect)
        {
            return cards;
        }
        var options = pile.Type == PileType.Draw ? SortByRarityThenId(cards) : cards;
        return Select(cs, ChoicePromptKind.PileSelect, source, pile.Type, options, prefs, ChoiceResultType.CombatCard, screenClipsMinSelect: true, selector);
    }

    public static IEnumerable<CardInstance> FromHand(CombatState cs, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(prefs);
        RequireSource(source);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var choice = cs.Choice;
        bool selector = choice.HasSelector;
        if (!selector)
        {
            choice.SignalPlayerChoiceBegun(cs, source.SiteId);
        }
        var cards = Where(player.Hand.Cards, filter ?? AnyCard);
        if (cards.Count == 0)
        {
            return cards;
        }
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
        {
            return cards;
        }
        return Select(cs, ChoicePromptKind.HandSelect, source, PileType.Hand, cards, prefs, ChoiceResultType.CombatCard, screenClipsMinSelect: false, selector);
    }

    public static IEnumerable<CardInstance> FromHandForDiscard(CombatState cs, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source)
        => FromHand(cs, player, prefs, filter, source);

    public static CardInstance? FromHandForUpgrade(CombatState cs, PlayerState player, ChoiceSource source)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        RequireSource(source);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return null;
        }
        var choice = cs.Choice;
        if (!choice.HasSelector)
        {
            choice.SignalPlayerChoiceBegun(cs, source.SiteId);
        }
        var cards = Where(player.Hand.Cards, Upgradable);
        if (cards.Count <= 1)
        {
            return cards.Count == 1 ? cards[0] : null;
        }
        var prompt = new ChoicePrompt(
            ChoicePromptKind.HandUpgrade,
            source.SiteId,
            source.SourceId,
            source.SourceInstanceId,
            PileType.Hand,
            Options(cards),
            1,
            1,
            false,
            false,
            false,
            true,
            ChoiceResultType.CombatCard);
        var answer = choice.Ask(prompt);
        return answer.Picks.Count > 0 ? cards[answer.Picks[0]] : null;
    }

    public static IReadOnlyList<CardInstance> SortByRarityThenId(IReadOnlyList<CardInstance> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        int n = cards.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }
        Array.Sort(order, (a, b) =>
        {
            var x = cards[a];
            var y = cards[b];
            int c = ((int)x.Rarity).CompareTo((int)y.Rarity);
            if (c != 0)
            {
                return c;
            }
            c = string.CompareOrdinal(x.Id, y.Id);
            return c != 0 ? c : a.CompareTo(b);
        });
        var sorted = new List<CardInstance>(n);
        for (int i = 0; i < n; i++)
        {
            sorted.Add(cards[order[i]]);
        }
        return sorted;
    }

    private static IEnumerable<CardInstance> Select(
        CombatState cs,
        ChoicePromptKind kind,
        ChoiceSource source,
        PileType? pile,
        IReadOnlyList<CardInstance> options,
        CardSelectorPrefs prefs,
        ChoiceResultType resultType,
        bool screenClipsMinSelect,
        bool selector)
    {
        int n = options.Count;
        int max = Math.Clamp(prefs.MaxSelect, 0, n);
        int min = Math.Min(Math.Clamp(prefs.MinSelect, 0, n), max);
        if (max == 0 && !prefs.RequireManualConfirmation)
        {
            if (!selector)
            {
                cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, EmptySelectionWithoutDecisionSite + source.SiteId);
            }
            return new List<CardInstance>();
        }
        if (!selector && !screenClipsMinSelect && prefs.RequireManualConfirmation && prefs.MinSelect > n)
        {
            cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, MinSelectClippedSite + source.SiteId);
        }
        var prompt = new ChoicePrompt(
            kind,
            source.SiteId,
            source.SourceId,
            source.SourceInstanceId,
            pile,
            Options(options),
            min,
            max,
            prefs.RequireManualConfirmation,
            false,
            prefs.Cancelable,
            true,
            resultType);
        var answer = cs.Choice.Ask(prompt);
        var picks = answer.Picks;
        var result = new List<CardInstance>(picks.Count);
        if (answer.Cancelled)
        {
            return result;
        }
        for (int i = 0; i < picks.Count; i++)
        {
            result.Add(options[picks[i]]);
        }
        return result;
    }

    private static List<CardInstance> Where(IReadOnlyList<CardInstance> cards, Func<CardInstance, bool> filter)
    {
        var result = new List<CardInstance>(cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (filter(card))
            {
                result.Add(card);
            }
        }
        return result;
    }

    private static ChoiceOption[] Options(IReadOnlyList<CardInstance> cards)
    {
        var options = new ChoiceOption[cards.Count];
        for (int i = 0; i < options.Length; i++)
        {
            var card = cards[i];
            options[i] = new ChoiceOption(card.InstanceId, card.Id, card.UpgradeLevel);
        }
        return options;
    }

    private static void RequireSource(ChoiceSource source)
    {
        if (string.IsNullOrEmpty(source.SiteId) || source.SourceId is null)
        {
            throw new ArgumentException("A card selection needs its ChoiceSource (site id '<model id>:<method>', source model id, source instance id); default(ChoiceSource) is rejected.", nameof(source));
        }
    }
}
