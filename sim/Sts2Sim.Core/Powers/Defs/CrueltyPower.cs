namespace Sts2Sim.Core.Powers;

public sealed class CrueltyPower : PowerModel, IVulnerableMultiplierModifier
{
    public const string PowerId = "CRUELTY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (!props.IsPoweredAttack())
        {
            return amount;
        }
        return amount + (decimal)Amount / 100m;
    }
}
