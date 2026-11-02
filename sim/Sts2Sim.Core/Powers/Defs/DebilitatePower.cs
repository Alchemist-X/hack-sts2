namespace Sts2Sim.Core.Powers;

public sealed class DebilitatePower : PowerModel, IVulnerableMultiplierModifier, IWeakMultiplierModifier
{
    public const string PowerId = "DEBILITATE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (!props.IsPoweredAttack())
        {
            return amount;
        }
        return amount + (amount - 1m);
    }

    public decimal ModifyWeakMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(dealer, Owner))
        {
            return amount;
        }
        if (!props.IsPoweredAttack())
        {
            return amount;
        }
        return amount - (1m - amount);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
