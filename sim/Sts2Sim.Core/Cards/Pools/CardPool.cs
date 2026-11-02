using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class CardEpochGate
{
    public CardEpochGate(string epochId, IReadOnlyList<string> cardIds)
    {
        EpochId = epochId;
        CardIds = cardIds;
    }

    public string EpochId { get; }

    public IReadOnlyList<string> CardIds { get; }

    public bool Gates(string cardId)
    {
        var ids = CardIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], cardId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class CardPool
{
    private readonly Lazy<ReadOnlyCollection<CardDef>> _allCards;

    internal CardPool(string id, string title, bool isColorless, string[] cardIds, CardEpochGate[] epochGates)
    {
        Id = id;
        Title = title;
        IsColorless = isColorless;
        AllCardIds = Array.AsReadOnly(cardIds);
        EpochGates = Array.AsReadOnly(epochGates);
        _allCards = new Lazy<ReadOnlyCollection<CardDef>>(ResolveCards);
    }

    public string Id { get; }

    public string Title { get; }

    public bool IsColorless { get; }

    public IReadOnlyList<string> AllCardIds { get; }

    public IReadOnlyList<CardDef> AllCards => _allCards.Value;

    public IReadOnlyList<CardEpochGate> EpochGates { get; }

    public bool Contains(string cardId)
    {
        var ids = AllCardIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], cardId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<CardDef> GetUnlockedCards(UnlockProfile unlocks, CardMultiplayerConstraint multiplayerConstraint)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        var cards = FilterThroughEpochs(unlocks, AllCards);
        switch (multiplayerConstraint)
        {
            case CardMultiplayerConstraint.MultiplayerOnly:
                cards.RemoveAll(c => c.MultiplayerConstraint == CardMultiplayerConstraint.SingleplayerOnly);
                break;
            case CardMultiplayerConstraint.SingleplayerOnly:
                cards.RemoveAll(c => c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly);
                break;
        }
        return cards;
    }

    public List<CardDef> FilterThroughEpochs(UnlockProfile unlocks, IEnumerable<CardDef> cards)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        ArgumentNullException.ThrowIfNull(cards);
        var list = new List<CardDef>(cards);
        var gates = EpochGates;
        for (int g = 0; g < gates.Count; g++)
        {
            var gate = gates[g];
            if (!unlocks.IsEpochRevealed(gate.EpochId))
            {
                list.RemoveAll(c => gate.Gates(c.Id));
            }
        }
        return list;
    }

    public override string ToString() => Id;

    private ReadOnlyCollection<CardDef> ResolveCards()
    {
        var ids = AllCardIds;
        var cards = new CardDef[ids.Count];
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i] = CardCatalog.Resolve(ids[i]);
        }
        return Array.AsReadOnly(cards);
    }
}
