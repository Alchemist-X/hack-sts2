namespace Sts2Sim.Core.Cards.Afflictions;

public abstract class AfflictionModel : AbstractModel, ICanonical
{
    public const int NoCard = -1;

    private CardInstance? _card;
    private int _amount;

    public abstract string Id { get; }

    public CardInstance? Card => _card;

    public bool HasCard => _card != null;

    public int Amount
    {
        get => _amount;
        set
        {
            if (_amount != value)
            {
                _amount = value;
                var card = _card;
                if (card != null)
                {
                    CardInstance.RecalculateCardValues(card.Owner);
                }
            }
        }
    }

    public virtual bool CanAfflictUnplayableCards => true;

    public virtual bool IsStackable => false;

    public bool IsLiveHookListener => _card is { } card && card.IsLiveHookListener;

    public virtual bool CanAfflictCardType(CardType cardType) => true;

    public virtual bool CanAfflict(CombatState? cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanAfflictCardType(card.Type))
        {
            return false;
        }
        if (card.HasKeywordInCombat(cs, CardKeyword.Unplayable) && !CanAfflictUnplayableCards)
        {
            return false;
        }
        var existing = card.Affliction;
        if (existing != null && (!IsStackable || existing.GetType() != GetType()))
        {
            return false;
        }
        return true;
    }

    public virtual void AfterApplied()
    {
    }

    public virtual void BeforeRemoved()
    {
    }

    public virtual void OnPlay(CombatState cs, CreatureState? target)
    {
    }

    public IReadOnlyList<CardInstance> PickRandomTargets(CombatState? cs, RunRngSet rngSet, IEnumerable<CardInstance> cards, int count)
    {
        ArgumentNullException.ThrowIfNull(rngSet);
        ArgumentNullException.ThrowIfNull(cards);
        var list = new List<CardInstance>();
        foreach (var card in cards)
        {
            if (CanAfflict(cs, card))
            {
                list.Add(card);
            }
        }
        list.UnstableShuffle(rngSet.CombatCardGeneration);
        list.RemoveRange(Math.Clamp(list.Count - 1, 0, count), Math.Max(0, list.Count - count));
        return list;
    }

    public void ClearInternal()
    {
        BeforeRemoved();
        _card = null;
    }

    internal void BindCard(CardInstance card)
    {
        if (_card != null && !ReferenceEquals(_card, card))
        {
            throw new InvalidOperationException("Afflictions cannot be moved from one card to another.");
        }
        _card = card;
    }

    public AfflictionModel MutableClone()
    {
        var clone = (AfflictionModel)MemberwiseClone();
        clone._card = null;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    public AfflictionModel CloneFor(CardInstance? newCard)
    {
        var clone = (AfflictionModel)MemberwiseClone();
        clone._card = _card is null ? null : newCard;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(AfflictionModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(AfflictionCanonicalTags.Affliction);
        w.WriteString(Id);
        w.WriteInt32(_card?.InstanceId ?? NoCard);
        w.WriteInt32(_amount);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => $"{Id}({Amount})";
}
