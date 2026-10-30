namespace Sts2Sim.Core.Powers;

public sealed class StrengthPower : PowerModel
{
    public const string PowerId = "STRENGTH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyDamageAdditive(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, dealer))
        {
            return 0m;
        }
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return Amount;
    }
}
