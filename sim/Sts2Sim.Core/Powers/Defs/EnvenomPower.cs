namespace Sts2Sim.Core.Powers;

public sealed class EnvenomPower : PowerModel
{
    public const string PowerId = "ENVENOM_POWER";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (ReferenceEquals(dealer, Owner) && props.IsPoweredAttack() && result.UnblockedDamage > 0)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), target, Amount, Owner, null);
        }
    }
}
