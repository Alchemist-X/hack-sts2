namespace Sts2Sim.Core.Powers;

public sealed class AccuracyPower : PowerModel
{
    public const string PowerId = "ACCURACY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, dealer))
        {
            return 0m;
        }
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        if (cardSource == null)
        {
            return 0m;
        }
        if (!cardSource.HasTag(CardTag.Shiv))
        {
            return 0m;
        }
        return Amount;
    }
}
