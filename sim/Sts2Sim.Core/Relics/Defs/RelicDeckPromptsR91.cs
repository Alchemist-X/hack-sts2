namespace Sts2Sim.Core.Relics;

internal static class RelicDeckPromptsR91
{
    public const string AfterObtainedSite = ":AfterObtained";

    public static string Site(string relicId) => relicId + AfterObtainedSite;

    public static IReadOnlyList<CardInstance> FromDeckForUpgrade(RunState rs, PlayerState player, CardSelectorPrefs prefs, string relicId)
        => RoomPrompts.FromDeckForUpgrade(rs, player, prefs, Site(relicId), relicId);

    public static IReadOnlyList<CardInstance> FromDeckForRemoval(RunState rs, PlayerState player, CardSelectorPrefs prefs, string relicId, Func<CardInstance, bool>? filter = null)
        => RoomPrompts.FromDeckForRemoval(rs, player, prefs, Site(relicId), relicId, filter);

    public static IReadOnlyList<CardInstance> FromDeckForTransformation(RunState rs, PlayerState player, CardSelectorPrefs prefs, string relicId)
    {
        var val = new List<CardInstance>();
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].Type != CardType.Quest && deck[i].IsTransformable(null))
            {
                val.Add(deck[i]);
            }
        }
        if (val.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (val.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return val;
        }
        return Ask(rs, ChoicePromptKind.DeckTransform, PileType.Deck, val, prefs, relicId);
    }

    public static IReadOnlyList<CardInstance> FromDeckForEnchantment(RunState rs, PlayerState player, EnchantmentModel enchantment, CardSelectorPrefs prefs, string relicId)
    {
        var cards = new List<CardInstance>();
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (enchantment.CanEnchant(deck[i]))
            {
                cards.Add(deck[i]);
            }
        }
        return FromDeckForEnchantment(rs, cards, enchantment, prefs, relicId);
    }

    public static IReadOnlyList<CardInstance> FromDeckForEnchantment(RunState rs, IReadOnlyList<CardInstance> cards, EnchantmentModel enchantment, CardSelectorPrefs prefs, string relicId)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (!cards[i].IsInDeck || !enchantment.CanEnchant(cards[i]))
            {
                throw new ArgumentException("All cards must be in the player's deck and enchantable.");
            }
        }
        var val = new List<CardInstance>();
        if (cards.Count > 0)
        {
            var deck = cards[0].Owner.Deck;
            val = cards.OrderBy(c => deck.IndexOf(c)).ToList();
        }
        if (cards.Count <= prefs.MinSelect)
        {
            return cards;
        }
        if (cards[0].Owner.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        return Ask(rs, ChoicePromptKind.DeckEnchant, PileType.Deck, val, prefs, relicId);
    }

    public static IReadOnlyList<CardInstance> FromDeckGeneric(RunState rs, PlayerState player, CardSelectorPrefs prefs, string relicId, Func<CardInstance, bool>? filter = null)
    {
        var val2 = filter == null ? player.Deck.ToList() : player.Deck.Where(filter).ToList();
        if (player.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        if (val2.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (!prefs.RequireManualConfirmation && val2.Count <= prefs.MinSelect)
        {
            return val2;
        }
        return Ask(rs, ChoicePromptKind.DeckGeneric, PileType.Deck, val2, prefs, relicId);
    }

    public static CardInstance? FromChooseACardScreen(RunState rs, IReadOnlyList<CardInstance> cards, bool canSkip, string relicId)
    {
        if (cards.Count > CardSelectCmd.MaxChooseACardOptions)
        {
            throw new ArgumentException("Only works with less than 3 cards", nameof(cards));
        }
        if (cards.Count == 0)
        {
            return null;
        }
        var prompt = new ChoicePrompt(ChoicePromptKind.ChooseCard, Site(relicId), relicId, ChoiceSource.NoInstance, null, Options(cards), canSkip ? 0 : 1, 1, false, canSkip, false, true, ChoiceResultType.Index);
        var answer = rs.Choice.Ask(prompt);
        return answer.Picks.Count > 0 ? cards[answer.Picks[0]] : null;
    }

    public static IReadOnlyList<CardInstance> FromSimpleGridForRewards(RunState rs, List<CardCreationResult> cards, CardSelectorPrefs prefs, string relicId)
    {
        if (rs.Combat != null && CombatStatus.IsEnding(rs.Combat))
        {
            return Array.Empty<CardInstance>();
        }
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        var list = cards.Select(c => c.Card).ToList();
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
        {
            return list;
        }
        return Ask(rs, ChoicePromptKind.RewardsGrid, null, list, prefs, relicId);
    }

    public static IReadOnlyList<CardInstance> FromChooseABundleScreen(RunState rs, IReadOnlyList<IReadOnlyList<CardInstance>> bundles, string relicId)
    {
        if (rs.Combat != null && CombatStatus.IsEnding(rs.Combat))
        {
            return Array.Empty<CardInstance>();
        }
        if (bundles.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        var options = new ChoiceOption[bundles.Count];
        for (int i = 0; i < bundles.Count; i++)
        {
            var b = bundles[i];
            options[i] = new ChoiceOption(b.Count > 0 ? b[0].InstanceId : -1, string.Join(",", b.Select(c => c.Id)), b.Count > 0 ? b[0].UpgradeLevel : 0);
        }
        var prompt = new ChoicePrompt(ChoicePromptKind.Bundle, Site(relicId), relicId, ChoiceSource.NoInstance, null, options, 1, 1, false, false, false, false, ChoiceResultType.Index);
        var answer = rs.Choice.Ask(prompt);
        if (answer.Cancelled || answer.Picks.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        return bundles[answer.Picks[0]];
    }

    public static CardInstance CloneCard(RunState rs, CardInstance mutableCard)
    {
        var clone = mutableCard.CloneForCombat(rs.AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
    }

    private static ChoiceOption[] Options(IReadOnlyList<CardInstance> cards)
    {
        var options = new ChoiceOption[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            options[i] = new ChoiceOption(cards[i].InstanceId, cards[i].Id, cards[i].UpgradeLevel);
        }
        return options;
    }

    private static IReadOnlyList<CardInstance> Ask(RunState rs, ChoicePromptKind kind, PileType? pile, List<CardInstance> cards, CardSelectorPrefs prefs, string relicId)
    {
        int max = Math.Min(prefs.MaxSelect, cards.Count);
        int min = Math.Min(prefs.MinSelect, max);
        var prompt = new ChoicePrompt(kind, Site(relicId), relicId, ChoiceSource.NoInstance, pile, Options(cards), min, max, prefs.RequireManualConfirmation, false, prefs.Cancelable, false, ChoiceResultType.Index);
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
