namespace Sts2Sim.Core.Powers;

public sealed class TrackingPower : PowerModel
{
    public const string PowerId = "TRACKING_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        if (!ReferenceEquals(dealer, Owner) && !IsOwnerPet(rs.Combat!, dealer))
        {
            return 1m;
        }
        if (target == null || !target.HasPower<WeakPower>())
        {
            return 1m;
        }
        return 1m + Amount / 100m;
    }

    private bool IsOwnerPet(CombatState cs, CreatureState? dealer)
        => dealer != null && ReferenceEquals(CombatStatus.PetOwnerOf(cs, dealer), Owner);
}
