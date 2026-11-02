namespace Sts2Sim.Core.Powers;

public sealed class ReattachPower : PowerModel
{
    public const string PowerId = "REATTACH_POWER";

    private bool _isReviving;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldScaleInMultiplayer => true;

    public bool IsReviving => _isReviving;

    public void DoReattach(CombatState cs)
    {
        if (!AreAllOtherSegmentsDead(cs))
        {
            _isReviving = false;
            CreatureCmd.Heal(cs, Owner, Amount);
        }
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(Owner, creature))
        {
            return;
        }
        var cs = rs.Combat!;
        if (AreAllOtherSegmentsDead(cs) && Owner.IsDead)
        {
            return;
        }
        _isReviving = true;
        if (creature is MonsterState monster && monster.Def is DecimillipedeSegment && monster.HasAi
            && monster.Ai.Machine.TryGet(DecimillipedeSegment.DeadMoveId, out var dead) && dead is MoveStateDef deadMove)
        {
            MonsterLifecycle.SetMoveImmediate(monster, deadMove);
        }
    }

    public override bool ShouldAllowHitting(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        return !_isReviving;
    }

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    public override bool ShouldOwnerDeathTriggerFatal(CombatState cs) => AreAllOtherSegmentsDead(cs);

    private bool AreAllOtherSegmentsDead(CombatState cs)
    {
        var teammates = cs.GetTeammatesOf(Owner);
        for (int i = 0; i < teammates.Count; i++)
        {
            var c = teammates[i];
            if (ReferenceEquals(c, Owner) || c.GetPower<ReattachPower>() == null)
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

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isReviving);
    }
}
