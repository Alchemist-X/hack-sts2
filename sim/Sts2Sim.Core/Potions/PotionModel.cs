namespace Sts2Sim.Core.Potions;

public abstract class PotionModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    private PlayerState? _owner;

    public abstract string Id { get; }

    public abstract PotionRarity Rarity { get; }

    public abstract PotionUsage Usage { get; }

    public abstract TargetType TargetType { get; }

    public virtual string Pool => throw new NotImplementedException("PotionModel.Pool: relics-potions / W2 potion pools (Models/PotionModel.cs:448)");

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Potion {Id} has no owner");

    public bool HasOwner => _owner != null;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public virtual bool CanBeGeneratedInCombat => true;

    public virtual bool PassesCustomUsabilityCheck => true;

    public bool HasBeenRemovedFromState { get; set; }

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move potion " + Id + " from one owner to another");
        }
        _owner = owner;
    }

    public bool IsValidTarget(CreatureState? target)
        => throw new NotImplementedException("PotionModel.IsValidTarget: relics-potions (Models/PotionModel.cs:618-654)");

    public void OnUseWrapper(RunState rs, CreatureState? target)
        => throw new NotImplementedException("PotionModel.OnUseWrapper: relics-potions (Models/PotionModel.cs:656)");

    protected virtual void OnUse(RunState rs, CreatureState? target)
    {
    }

    public PotionModel CloneFor(PlayerState? newOwner)
    {
        var clone = (PotionModel)MemberwiseClone();
        clone._owner = _owner is null ? null : newOwner;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(PotionModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(PotionCanonicalTags.Potion);
        w.WriteString(Id);
        w.WriteInt32(_owner?.CombatId ?? NoOwner);
        w.WriteBool(HasBeenRemovedFromState);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => Id;
}
