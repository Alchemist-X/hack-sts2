namespace Sts2Sim.Core.Powers;

public sealed class FlameBarrierPower : PowerModel
{
    public const string PowerId = "FLAME_BARRIER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner) && dealer != null && props.IsPoweredAttack())
        {
            CreatureCmd.Damage(rs.Combat!, dealer, Amount, ValueProp.Unpowered, Owner);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (Owner.Side != side)
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
