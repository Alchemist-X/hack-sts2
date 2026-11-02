namespace Sts2Sim.Core.Choice;

public static partial class CardSelectCmd
{
    public static IReadOnlyList<CardInstance> FromDeckForUpgrade(RunState rs, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source)
    {
        var cards = player.Deck.Where(c => c.IsUpgradable).ToList();
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (cards.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return cards;
        }
        return AskDeck(rs, ChoicePromptKind.DeckUpgrade, cards, prefs, source);
    }

    public static IReadOnlyList<CardInstance> FromDeckForTransformation(RunState rs, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source)
    {
        var cards = player.Deck.Where(c => c.Type != CardType.Quest && c.IsTransformable(null)).ToList();
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (cards.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return cards;
        }
        return AskDeck(rs, ChoicePromptKind.DeckTransform, cards, prefs, source);
    }

    public static IReadOnlyList<CardInstance> FromDeckForEnchantment(RunState rs, PlayerState player, EnchantmentModel enchantment, int amount, CardSelectorPrefs prefs, ChoiceSource source, Func<CardInstance, bool>? additionalFilter = null)
    {
        ArgumentNullException.ThrowIfNull(enchantment);
        var cards = player.Deck.Where(c => enchantment.CanEnchant(c) && (additionalFilter == null || additionalFilter(c))).ToList();
        if (cards.Count <= prefs.MinSelect)
        {
            return cards;
        }
        if (player.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        return AskDeck(rs, ChoicePromptKind.DeckEnchant, cards, prefs, source);
    }

    public static IReadOnlyList<CardInstance> FromDeckForRemoval(RunState rs, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source, Func<CardInstance, bool>? filter = null)
    {
        var deck = player.Deck.ToList();
        return FromDeckGeneric(rs, player, prefs, source, c => c.IsRemovable && (filter == null || filter(c)), c => c.Type != CardType.Curse ? deck.IndexOf(c) : -999999999);
    }

    public static IReadOnlyList<CardInstance> FromDeckGeneric(RunState rs, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source, Func<CardInstance, bool>? filter = null, Func<CardInstance, int>? sortingOrder = null)
    {
        var cards = filter == null ? player.Deck.ToList() : player.Deck.Where(filter).ToList();
        if (player.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        if (sortingOrder != null)
        {
            cards = cards.OrderBy(sortingOrder).ToList();
        }
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
        {
            return cards;
        }
        return AskDeck(rs, ChoicePromptKind.DeckGeneric, cards, prefs, source);
    }

    private static IReadOnlyList<CardInstance> AskDeck(RunState rs, ChoicePromptKind kind, List<CardInstance> cards, CardSelectorPrefs prefs, ChoiceSource source)
    {
        var options = new ChoiceOption[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            options[i] = new ChoiceOption(cards[i].InstanceId, cards[i].Id, cards[i].UpgradeLevel);
        }
        int max = Math.Min(prefs.MaxSelect, cards.Count);
        int min = Math.Min(prefs.MinSelect, max);
        var prompt = new ChoicePrompt(kind, source.SiteId, source.SourceId, source.SourceInstanceId, PileType.Deck, options, min, max, prefs.RequireManualConfirmation, false, prefs.Cancelable, false, ChoiceResultType.Index);
        var answer = rs.Choice.Ask(prompt);
        if (answer.Cancelled || answer.Picks.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        var picked = new List<CardInstance>(answer.Picks.Count);
        for (int i = 0; i < answer.Picks.Count; i++)
        {
            picked.Add(cards[answer.Picks[i]]);
        }
        return picked;
    }
}
