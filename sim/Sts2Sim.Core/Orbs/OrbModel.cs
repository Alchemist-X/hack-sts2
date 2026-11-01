namespace Sts2Sim.Core.Orbs;

public abstract class OrbModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    private PlayerState? _owner;

    public abstract string Id { get; }

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Orb {Id} has no owner");

    public bool HasOwner => _owner != null;

    public bool HasBeenRemovedFromState { get; set; }

    public abstract decimal PassiveVal(CombatState cs);

    public abstract decimal EvokeVal(CombatState cs);

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move orb " + Id + " from one owner to another");
        }
        _owner = owner;
    }

    public virtual void BeforeTurnEndOrbTrigger(CombatState cs)
    {
    }

    public virtual void AfterTurnStartOrbTrigger(CombatState cs)
    {
    }

    public void TriggerPassive(CombatState cs, CreatureState? target)
        => throw new NotImplementedException("OrbModel.TriggerPassive: character-mechanics (Models/OrbModel.cs:481)");

    public virtual void Passive(CombatState cs, CreatureState? target)
    {
    }

    public virtual IEnumerable<CreatureState> Evoke(CombatState cs) => Array.Empty<CreatureState>();

    protected decimal ModifyOrbValue(CombatState cs, decimal result) => Hook.ModifyOrbValue(cs, this, result);

    public void RemoveInternal()
    {
        HasBeenRemovedFromState = true;
    }

    public OrbModel CloneFor(PlayerState? newOwner)
    {
        var clone = (OrbModel)MemberwiseClone();
        clone._owner = _owner is null ? null : newOwner;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(OrbModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(OrbCanonicalTags.Orb);
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
