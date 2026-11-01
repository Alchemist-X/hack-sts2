namespace Sts2Sim.Core.Cards.Afflictions;

public abstract class AfflictionModel : AbstractModel, ICanonical
{
    public const int NoCard = -1;

    private CardInstance? _card;

    public abstract string Id { get; }

    public CardInstance Card => _card ?? throw new InvalidOperationException($"Affliction {Id} is not on a card");

    public bool HasCard => _card != null;

    public int Amount { get; set; }

    public virtual bool CanAfflictUnplayableCards => true;

    public virtual bool IsStackable => false;

    public virtual bool CanAfflictCardType(CardType cardType) => true;

    public virtual bool CanAfflict(CardInstance card)
        => throw new NotImplementedException("AfflictionModel.CanAfflict: cards-kernel (Models/AfflictionModel.cs:200-215)");

    public virtual void AfterApplied()
    {
    }

    public virtual void BeforeRemoved()
    {
    }

    public virtual void OnPlay(CombatState cs, CreatureState? target)
    {
    }

    public IReadOnlyList<CardInstance> PickRandomTargets(RunRngSet rngSet, IEnumerable<CardInstance> cards, int count)
        => throw new NotImplementedException("AfflictionModel.PickRandomTargets: cards-kernel (Models/AfflictionModel.cs:245-250)");

    public void ClearInternal()
        => throw new NotImplementedException("AfflictionModel.ClearInternal: cards-kernel (Models/AfflictionModel.cs:252-256)");

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
        w.WriteInt32(Amount);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => $"{Id}({Amount})";
}
