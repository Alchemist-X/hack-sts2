namespace Sts2Sim.Core.Powers;

public sealed class AdaptablePower : PowerModel
{
    public const string PowerId = "ADAPTABLE_POWER";

    private bool _isReviving;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public bool IsReviving => _isReviving;

    public void DoRevive()
    {
        _isReviving = false;
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner) || creature is not MonsterState monster || monster.Def is not TestSubject)
        {
            return;
        }
        _isReviving = true;
        TestSubject.TriggerDeadState(rs.Combat!, monster);
    }

    public override bool ShouldAllowHitting(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        return !_isReviving;
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isReviving);
    }
}
