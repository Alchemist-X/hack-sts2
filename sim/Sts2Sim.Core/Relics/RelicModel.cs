namespace Sts2Sim.Core.Relics;

public abstract class RelicModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    private PlayerState? _owner;

    public abstract string Id { get; }

    public abstract RelicRarity Rarity { get; }

    public virtual string Pool => throw new NotImplementedException("RelicModel.Pool: relics-potions / W2 relic pools (Models/RelicModel.cs:192)");

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Relic {Id} has no owner");

    public bool HasOwner => _owner != null;

    public bool IsWax { get; set; }

    public bool IsMelted { get; set; }

    public int StackCount { get; set; } = 1;

    public int FloorAddedToDeck { get; set; }

    public RelicStatus Status { get; set; }

    public bool HasBeenRemovedFromState { get; set; }

    public virtual bool IsAllowedInShops => true;

    public virtual bool IsUsedUp => false;

    public virtual bool HasUponPickupEffect => false;

    public virtual bool SpawnsPets => false;

    public virtual bool IsStackable => false;

    public virtual bool AddsPet => false;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public virtual int MerchantCost => throw new NotImplementedException("RelicModel.MerchantCost: relics-potions / W2 (Models/RelicModel.cs:307-318)");

    public virtual bool ShowCounter => false;

    public virtual int DisplayAmount => 0;

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move relic from " + Id + " one owner to another");
        }
        _owner = owner;
    }

    public virtual bool IsAllowed(RunState rs) => true;

    public virtual bool IsAllowedAtNeow(RunState rs, PlayerState player) => IsAllowed(rs);

    public virtual void AfterObtained(RunState rs)
    {
    }

    public virtual void AfterRemoved(RunState rs)
    {
    }

    public RelicModel CloneFor(PlayerState? newOwner)
    {
        var clone = (RelicModel)MemberwiseClone();
        clone._owner = _owner is null ? null : newOwner;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(RelicModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RelicCanonicalTags.Relic);
        w.WriteString(Id);
        w.WriteInt32(_owner?.CombatId ?? NoOwner);
        w.WriteBool(IsWax);
        w.WriteBool(IsMelted);
        w.WriteInt32(StackCount);
        w.WriteInt32(FloorAddedToDeck);
        w.WriteByte((byte)Status);
        w.WriteBool(HasBeenRemovedFromState);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => Id;
}
