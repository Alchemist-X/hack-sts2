namespace Sts2Sim.Core.Powers;

public sealed class ReflectPower : PowerModel
{
    public const string PowerId = "REFLECT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner) && result.BlockedDamage > 0 && props.IsPoweredAttack() && dealer != null)
        {
            CreatureCmd.Damage(rs.Combat!, dealer, result.BlockedDamage, ValueProp.Unpowered, Owner);
        }
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
