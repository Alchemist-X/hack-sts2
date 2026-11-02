namespace Sts2Sim.Core.Powers;

public sealed class SoarPower : PowerModel
{
    public const string PowerId = "SOAR_POWER";

    public const decimal DamageDecrease = 50m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return DamageDecrease / 100m;
    }
}
