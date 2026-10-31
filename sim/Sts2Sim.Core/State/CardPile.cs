namespace Sts2Sim.Core.State;

public sealed class CardPile : ICanonical
{
    public const int MaxCardsInHand = 10;

    private const int MinCloneCapacity = 8;

    private readonly List<CardInstance> _cards;

    public CardPile(PileType type, int capacity = 16)
    {
        Type = type;
        _cards = new List<CardInstance>(Math.Max(0, capacity));
    }

    private CardPile(PileType type, List<CardInstance> cards)
    {
        Type = type;
        _cards = cards;
    }

    public PileType Type { get; }

    public IReadOnlyList<CardInstance> Cards => _cards;

    public List<CardInstance> MutableCards => _cards;

    public int Count => _cards.Count;

    public bool IsEmpty => _cards.Count == 0;

    public bool IsCombatPile => IsCombatPileType(Type);

    public CardInstance this[int index] => _cards[index];

    public static bool IsCombatPileType(PileType type) => (uint)(type - 1) <= 4u;

    public int IndexOf(CardInstance card)
    {
        var cards = _cards;
        for (int i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
            {
                return i;
            }
        }
        return -1;
    }

    public bool Contains(CardInstance card) => IndexOf(card) >= 0;

    public void AddInternal(CardInstance card, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (Contains(card))
        {
            throw new InvalidOperationException($"Card pile {Type} already contains {Describe(card)}.");
        }
        if (index >= 0)
        {
            _cards.Insert(index, card);
        }
        else
        {
            _cards.Add(card);
        }
    }

    public bool RemoveInternal(CardInstance card)
    {
        int index = IndexOf(card);
        if (index < 0)
        {
            throw new InvalidOperationException($"Card pile {Type} does not contain {Describe(card)}.");
        }
        _cards.RemoveAt(index);
        return true;
    }

    public bool TryRemoveInternal(CardInstance card)
    {
        int index = IndexOf(card);
        if (index < 0)
        {
            return false;
        }
        _cards.RemoveAt(index);
        return true;
    }

    public CardInstance RemoveAtInternal(int index)
    {
        var card = _cards[index];
        _cards.RemoveAt(index);
        return card;
    }

    public void MoveToBottomInternal(CardInstance card)
    {
        int index = IndexOf(card);
        if (index < 0)
        {
            throw new InvalidOperationException($"Card pile {Type} does not contain {Describe(card)}.");
        }
        _cards.RemoveAt(index);
        _cards.Add(card);
    }

    public void MoveToTopInternal(CardInstance card)
    {
        int index = IndexOf(card);
        if (index < 0)
        {
            throw new InvalidOperationException($"Card pile {Type} does not contain {Describe(card)}.");
        }
        _cards.RemoveAt(index);
        _cards.Insert(0, card);
    }

    public void ClearInternal() => _cards.Clear();

    public CardPile DeepClone()
    {
        var source = _cards;
        var cards = new List<CardInstance>(Math.Max(source.Count, MinCloneCapacity));
        for (int i = 0; i < source.Count; i++)
        {
            cards.Add(source[i].DeepClone());
        }
        return new CardPile(Type, cards);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.CardPile);
        w.WriteInt32((int)Type);
        var cards = _cards;
        w.WriteCount(cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].WriteCanonical(w);
        }
    }

    private static string Describe(CardInstance? card) => card is null ? "null" : $"{card.Id}#{card.InstanceId}";
}
