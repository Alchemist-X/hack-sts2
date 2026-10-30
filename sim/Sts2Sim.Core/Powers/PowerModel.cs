namespace Sts2Sim.Core.Powers;

public static class PowerCanonicalTags
{
    public const ushort Power = 0x5401;
}

public abstract class PowerModel : AbstractModel, ICanonical
{
    public const int NoCreature = -1;

    public const int MaxAmount = 999999999;

    private CreatureState? _owner;

    public abstract string Id { get; }

    public abstract PowerType Type { get; }

    public abstract PowerStackType StackType { get; }

    public virtual PowerInstanceType InstanceType => PowerInstanceType.None;

    public virtual bool AllowNegative => false;

    public virtual bool OwnerIsSecondaryEnemy => false;

    public int Amount { get; private set; }

    public int AmountOnTurnStart { get; set; }

    public bool SkipNextDurationTick { get; set; }

    public int ApplierId { get; set; } = NoCreature;

    public int TargetId { get; set; } = NoCreature;

    public CreatureState Owner => _owner ?? throw new InvalidOperationException($"Power {Id} has no owner");

    public bool HasOwner => _owner != null;

    public PowerType TypeForCurrentAmount => GetTypeForAmount(Amount);

    public void AttachTo(CreatureState owner)
    {
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move power " + Id + " from one owner to another");
        }
        _owner = owner;
    }

    public void SetAmount(int amount)
    {
        Amount = Math.Clamp(amount, -MaxAmount, MaxAmount);
    }

    public PowerType GetTypeForAmount(decimal customAmount)
    {
        if (StackType == PowerStackType.Counter && AllowNegative && customAmount < 0m)
        {
            return PowerType.Debuff;
        }
        if (!AllowNegative && Type == PowerType.Debuff && customAmount < 0m)
        {
            return PowerType.Buff;
        }
        return Type;
    }

    public bool ShouldRemoveDueToAmount()
    {
        if (AllowNegative || Amount > 0)
        {
            if (AllowNegative)
            {
                return Amount == 0;
            }
            return false;
        }
        return true;
    }

    public void ApplyInternal(CreatureState owner, decimal amount)
    {
        if (amount == 0m)
        {
            return;
        }
        AttachTo(owner);
        SetAmount((int)amount);
        var powers = owner.Powers;
        if (InstanceType == PowerInstanceType.None)
        {
            var type = GetType();
            for (int i = 0; i < powers.Count; i++)
            {
                if (powers[i].GetType() == type)
                {
                    throw new InvalidOperationException("Trying to add multiple instances of a non-instanced power to a creature.");
                }
            }
        }
        powers.Add(this);
    }

    public void RemoveInternal()
    {
        Owner.Powers.Remove(this);
    }

    public virtual void BeforeApplied(CombatState cs, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
    }

    public virtual bool ShouldPowerBeRemovedAfterOwnerDeath() => true;

    public virtual bool ShouldOwnerDeathTriggerFatal() => true;

    public PowerModel CloneFor(CreatureState newOwner)
    {
        var clone = (PowerModel)MemberwiseClone();
        clone._owner = newOwner;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(PowerModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(PowerCanonicalTags.Power);
        w.WriteString(Id);
        w.WriteInt32(Amount);
        w.WriteInt32(AmountOnTurnStart);
        w.WriteBool(SkipNextDurationTick);
        w.WriteInt32(ApplierId);
        w.WriteInt32(TargetId);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => $"{Id}({Amount})";
}
