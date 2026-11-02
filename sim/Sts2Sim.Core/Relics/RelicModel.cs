namespace Sts2Sim.Core.Relics;

public abstract class RelicModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    public const int UnavailableMerchantCost = 999999999;

    private PlayerState? _owner;
    private DynamicVarSet _dynamicVars;

    protected RelicModel()
    {
        _dynamicVars = new DynamicVarSet(CanonicalVars);
    }

    public abstract string Id { get; }

    public abstract RelicRarity Rarity { get; }

    public string Pool => RelicPools.PoolOf(Id);

    public bool IsTradable
    {
        get
        {
            if (IsUsedUp)
            {
                return false;
            }
            if (HasUponPickupEffect)
            {
                return false;
            }
            if (IsMelted)
            {
                return false;
            }
            if (SpawnsPets)
            {
                return false;
            }
            var rarity = Rarity;
            if (rarity == RelicRarity.Starter || (uint)(rarity - RelicRarity.Event) <= 1u)
            {
                return false;
            }
            return true;
        }
    }

    public virtual bool IsAllowedInShops => true;

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Relic {Id} has no owner");

    public bool HasOwner => _owner != null;

    public virtual bool IsUsedUp => false;

    public virtual bool HasUponPickupEffect => false;

    public virtual bool SpawnsPets => false;

    public virtual bool IsStackable => false;

    public bool IsWax { get; set; }

    public bool IsMelted { get; set; }

    public virtual bool AddsPet => false;

    public int StackCount { get; private set; } = 1;

    public DynamicVarSet DynamicVars => _dynamicVars;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public virtual int MerchantCost => Rarity switch
    {
        RelicRarity.Common => 175,
        RelicRarity.Uncommon => 225,
        RelicRarity.Rare => 275,
        RelicRarity.Shop => 200,
        RelicRarity.Ancient => UnavailableMerchantCost,
        RelicRarity.Starter => UnavailableMerchantCost,
        RelicRarity.Event => UnavailableMerchantCost,
        RelicRarity.None => 1,
        _ => throw new InvalidOperationException($"Relic {Id} has invalid merchant rarity {Rarity}.")
    };

    public int FloorAddedToDeck { get; set; }

    public RelicStatus Status { get; set; }

    public virtual bool ShowCounter => false;

    public virtual int DisplayAmount => 0;

    public bool HasBeenRemovedFromState { get; private set; }

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

    protected static bool IsBeforeAct3TreasureChest(RunState rs)
        => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    protected static bool IsBeforeAct3TreasureChest(int playerCount, int totalFloor)
    {
        int num = playerCount > 1 ? 38 : 41;
        return totalFloor < num;
    }

    public void RemoveInternal()
    {
        HasBeenRemovedFromState = true;
    }

    public void IncrementStackCount()
    {
        if (!IsStackable)
        {
            throw new InvalidOperationException($"Cannot increment stack count on {Id} because it is not a stackable relic.");
        }
        StackCount++;
    }

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
        clone._dynamicVars = _dynamicVars.DeepClone();
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
        w.WriteTag(RelicCanonicalTags.DynamicVars);
        _dynamicVars.WriteCanonical(w);
        w.WriteTag(RelicCanonicalTags.Extra);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => Id;
}
