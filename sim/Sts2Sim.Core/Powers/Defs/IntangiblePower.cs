namespace Sts2Sim.Core.Powers;

public sealed class IntangiblePower : PowerModel
{
    public const string PowerId = "INTANGIBLE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHpLostAfterOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (rs.Combat is not { IsInProgress: true })
        {
            return amount;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (amount < 1m)
        {
            return amount;
        }
        return 1m;
    }

    public override decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return decimal.MaxValue;
        }
        return 1m;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
