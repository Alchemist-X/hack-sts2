namespace Sts2Sim.Core.Powers;

public sealed class AdaptablePower : PowerModel
{
    public const string PowerId = "ADAPTABLE_POWER";

    public const string TestSubjectId = "TEST_SUBJECT";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public bool IsReviving { get; private set; }

    public void DoRevive()
    {
        IsReviving = false;
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (!wasRemovalPrevented && ReferenceEquals(creature, Owner) && MonsterPowerCoupling.Is(creature, TestSubjectId, out var testSubject))
        {
            IsReviving = true;
            MonsterPowerCoupling.Call(rs.Combat!, testSubject, "TriggerDeadState", PowerId);
        }
    }

    public override bool ShouldAllowHitting(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        return !IsReviving;
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature)
        => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(IsReviving);
    }
}
