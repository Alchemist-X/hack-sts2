namespace Sts2Sim.Core.Powers;

public sealed class ConstrictPower : PowerModel
{
    public const string PowerId = "CONSTRICT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            CreatureCmd.Damage(cs, Owner, Amount, ValueProp.Unpowered, Owner);
        }
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        var cs = rs.Combat!;
        if (!wasRemovalPrevented && ReferenceEquals(creature, GetApplier(cs)))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
