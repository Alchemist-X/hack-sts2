namespace Sts2Sim.Core.Powers;

public sealed class ShrinkPower : PowerModel
{
    public const string PowerId = "SHRINK_POWER";

    public const decimal DamageDecrease = 30m;

    public const decimal DamageMultiplier = (100m - DamageDecrease) / 100m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => IsInfinite ? PowerStackType.Single : PowerStackType.Counter;

    public override bool AllowNegative => true;

    public bool IsInfinite => Amount < 0;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side)
    {
        if (!IsInfinite && IsTurnParticipant(cs, side))
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    public override void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (!wasRemovalPrevented && ApplierId != NoCreature && creature.CombatId == ApplierId)
        {
            PowerCmd.Remove(cs, this);
        }
    }

    public override decimal ModifyDamageMultiplicative(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, dealer))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return DamageMultiplier;
    }

    public bool IsTurnParticipant(CombatState cs, CombatSide side)
    {
        var owner = Owner;
        return side switch
        {
            CombatSide.Player => ReferenceEquals(owner, cs.Player),
            CombatSide.Enemy => owner is MonsterState monster && cs.Monsters.Contains(monster),
            _ => false
        };
    }
}
