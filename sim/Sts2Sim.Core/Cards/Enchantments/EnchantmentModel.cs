namespace Sts2Sim.Core.Cards.Enchantments;

public abstract class EnchantmentModel : AbstractModel, ICanonical
{
    public const int NoCard = -1;

    private CardInstance? _card;
    private DynamicVarSet? _dynamicVars;

    public abstract string Id { get; }

    public virtual bool ShouldStartAtBottomOfDrawPile => false;

    public CardInstance? Card => _card;

    public bool HasCard => _card != null;

    public int Amount { get; set; }

    public virtual bool IsStackable => false;

    public DynamicVarSet DynamicVars => _dynamicVars ??= CreateDynamicVars();

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    private DynamicVarSet CreateDynamicVars()
    {
        var vars = new DynamicVarSet(CanonicalVars);
        vars.InitializeWithOwner();
        return vars;
    }

    public EnchantmentStatus Status { get; set; }

    public bool IsLiveHookListener => _card is { } card && card.IsLiveHookListener;

    public virtual bool CanEnchantCardType(CardType cardType) => true;

    public virtual bool CanEnchant(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var type = card.Type;
        if ((uint)(type - CardType.Status) <= 2u)
        {
            return false;
        }
        if (!CanEnchantCardType(card.Type))
        {
            return false;
        }
        if (card.IsInDeck && card.HasKeyword(CardKeyword.Unplayable))
        {
            return false;
        }
        var existing = card.Enchantment;
        if (existing != null && (!IsStackable || existing.GetType() != GetType()))
        {
            return false;
        }
        return true;
    }

    public virtual void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
    }

    public void ApplyInternal(CardInstance card, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_card != null)
        {
            throw new InvalidOperationException("Can't apply an enchantment to a card when it's already been applied to a different card.");
        }
        Amount = (int)amount;
        _card = card;
    }

    public void ClearInternal()
    {
        _card = null;
    }

    public void ModifyCard()
    {
        var card = _card ?? throw new InvalidOperationException("Card must be set at this point.");
        OnEnchant();
        RecalculateValues();
        card.Vars.RecalculateForUpgradeOrEnchant();
    }

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

    internal void BindCard(CardInstance card)
    {
        if (_card != null && !ReferenceEquals(_card, card))
        {
            throw new InvalidOperationException("Enchantments cannot be moved from one card to another.");
        }
        _card = card;
    }

    public EnchantmentModel MutableClone()
    {
        var clone = (EnchantmentModel)MemberwiseClone();
        clone._card = null;
        clone._dynamicVars = DynamicVars.CloneForOwner();
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    public EnchantmentModel CloneFor(CardInstance? newCard)
    {
        var clone = (EnchantmentModel)MemberwiseClone();
        clone._card = _card is null ? null : newCard;
        clone._dynamicVars = DynamicVars.DeepClone();
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
        DynamicVars.WriteCanonical(w);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => $"{Id}({Amount})";
}
