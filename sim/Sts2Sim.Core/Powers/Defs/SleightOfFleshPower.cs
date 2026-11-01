namespace Sts2Sim.Core.Powers;

public sealed class SleightOfFleshPower : PowerModel
{
    public const string PowerId = "SLEIGHT_OF_FLESH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (amount == 0m)
        {
            return;
        }
        if (power.GetTypeForAmount(amount) != PowerType.Debuff || !power.Owner.IsEnemy || !ReferenceEquals(applier, Owner) || power is ITemporaryPower)
        {
            return;
        }
        CreatureCmd.Damage(cs, power.Owner, Amount, ValueProp.Unpowered, Owner);
    }
}
