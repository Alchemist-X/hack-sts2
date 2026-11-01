namespace Sts2Sim.Core.Run;

public static class RoomPrompts
{
    public const string MerchantRemovalSite = "OneOffSynchronizer.DoMerchantCardRemoval";

    public const string SmithSite = "SmithRestSiteOption.OnSelect";

    public const string CookSite = "CookRestSiteOption.OnSelect";

    public static IReadOnlyList<CardInstance> FromDeckForUpgrade(RunState rs, PlayerState player, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        var cards = new List<CardInstance>();
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].IsUpgradable)
            {
                cards.Add(deck[i]);
            }
        }
        return Select(rs, ChoicePromptKind.DeckUpgrade, cards, prefs, siteId, sourceId);
    }

    public static IReadOnlyList<CardInstance> FromDeckForRemoval(RunState rs, PlayerState player, CardSelectorPrefs prefs, string siteId, string sourceId, Func<CardInstance, bool>? filter = null)
    {
        var deck = player.Deck;
        var filtered = new List<CardInstance>();
        for (int i = 0; i < deck.Count; i++)
        {
            var card = deck[i];
            if (card.IsRemovable && (filter == null || filter(card)))
            {
                filtered.Add(card);
            }
        }
        if (player.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        var sorted = filtered.OrderBy(c => c.Type != CardType.Curse ? deck.IndexOf(c) : -999999999).ToList();
        return Select(rs, ChoicePromptKind.DeckRemove, sorted, prefs, siteId, sourceId);
    }

    private static IReadOnlyList<CardInstance> Select(RunState rs, ChoicePromptKind kind, List<CardInstance> cards, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (cards.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return cards;
        }
        var options = new ChoiceOption[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            options[i] = new ChoiceOption(cards[i].InstanceId, cards[i].Id, cards[i].UpgradeLevel);
        }
        int max = Math.Min(prefs.MaxSelect, cards.Count);
        int min = Math.Min(prefs.MinSelect, max);
        var prompt = new ChoicePrompt(kind, siteId, sourceId, -1, PileType.Deck, options, min, max, prefs.RequireManualConfirmation, false, prefs.Cancelable, false, ChoiceResultType.Index);
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
