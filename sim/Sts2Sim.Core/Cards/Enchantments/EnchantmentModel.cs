namespace Sts2Sim.Core.Cards.Enchantments;

public abstract class EnchantmentModel : AbstractModel, ICanonical
{
    public const int NoCard = -1;

    private CardInstance? _card;

    public abstract string Id { get; }

    public virtual bool ShouldStartAtBottomOfDrawPile => false;

    public CardInstance Card => _card ?? throw new InvalidOperationException($"Enchantment {Id} is not on a card");

    public bool HasCard => _card != null;

    public int Amount { get; set; }

    public virtual bool IsStackable => false;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public EnchantmentStatus Status { get; set; }

    public virtual bool CanEnchantCardType(CardType cardType) => true;

    public virtual bool CanEnchant(CardInstance card)
        => throw new NotImplementedException("EnchantmentModel.CanEnchant: cards-kernel (Models/EnchantmentModel.cs:294-315)");

    public virtual void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
    }

    public void ApplyInternal(CardInstance card, decimal amount)
        => throw new NotImplementedException("EnchantmentModel.ApplyInternal: cards-kernel (Models/EnchantmentModel.cs:337-348)");

    public void ClearInternal()
        => throw new NotImplementedException("EnchantmentModel.ClearInternal: cards-kernel (Models/EnchantmentModel.cs:350-354)");

    public void ModifyCard()
        => throw new NotImplementedException("EnchantmentModel.ModifyCard: cards-kernel (Models/EnchantmentModel.cs:356-366)");

    public virtual void RecalculateValues()
    {
    }

    protected virtual void OnEnchant()
    {
    }

    public virtual decimal EnchantBlockAdditive(decimal originalBlock) => 0m;

    public virtual decimal EnchantBlockMultiplicative(decimal originalBlock) => 1m;

    public virtual decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props) => 0m;

    public virtual decimal EnchantDamageMultiplicative(decimal originalDamage, ValueProp props) => 1m;

    public virtual int EnchantPlayCount(int originalPlayCount) => originalPlayCount;

    public EnchantmentModel CloneFor(CardInstance? newCard)
    {
        var clone = (EnchantmentModel)MemberwiseClone();
        clone._card = _card is null ? null : newCard;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(EnchantmentModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(EnchantmentCanonicalTags.Enchantment);
        w.WriteString(Id);
        w.WriteInt32(_card?.InstanceId ?? NoCard);
        w.WriteInt32(Amount);
        w.WriteByte((byte)Status);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => $"{Id}({Amount})";
}
