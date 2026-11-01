namespace Sts2Sim.Core.Powers;

public sealed class ReattachPower : PowerModel
{
    public const string PowerId = "REATTACH_POWER";

    public const string DecimillipedeSegmentId = "DECIMILLIPEDE_SEGMENT";

    public const string DeadStateId = "DEAD_MOVE";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldScaleInMultiplayer => true;

    public bool IsReviving { get; private set; }

    public bool AllOtherSegmentsDeadCached { get; private set; } = true;

    public void DoReattach(CombatState cs)
    {
        if (AreAllOtherSegmentsDead(cs))
        {
            return;
        }
        IsReviving = false;
        CreatureCmd.Heal(cs, Owner, Amount);
    }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        Refresh(cs);
    }

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        Refresh(cs);
    }

    public override void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
        Refresh(cs);
    }

    public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
    {
        if (rs.Combat is { } cs)
        {
            Refresh(cs);
        }
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        var cs = rs.Combat!;
        Refresh(cs);
        if (wasRemovalPrevented || !ReferenceEquals(Owner, creature))
        {
            return;
        }
        if (AreAllOtherSegmentsDead(cs) && Owner.IsDead)
        {
            return;
        }
        IsReviving = true;
        if (MonsterPowerCoupling.Is(creature, DecimillipedeSegmentId, out var segment))
        {
            MonsterLifecycle.SetMoveImmediate(segment, MonsterPowerCoupling.MachineMove(segment, DeadStateId));
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

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature)
        => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    public override bool ShouldOwnerDeathTriggerFatal() => AllOtherSegmentsDeadCached;

    public bool AreAllOtherSegmentsDead(CombatState cs)
    {
        var teammates = cs.GetTeammatesOf(Owner);
        for (int i = 0; i < teammates.Count; i++)
        {
            var c = teammates[i];
            if (ReferenceEquals(c, Owner) || !c.HasPower<ReattachPower>())
            {
                continue;
            }
            if (!c.IsDead)
            {
                return false;
            }
        }
        return true;
    }

    private void Refresh(CombatState cs)
    {
        if (HasOwner)
        {
            AllOtherSegmentsDeadCached = AreAllOtherSegmentsDead(cs);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(IsReviving);
        w.WriteBool(AllOtherSegmentsDeadCached);
    }
}
