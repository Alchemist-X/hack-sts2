namespace Sts2Sim.Core.Powers;

public sealed class IllusionPower : PowerModel
{
    public const string PowerId = "ILLUSION_POWER";
    public const string ReviveMoveId = "REVIVE_MOVE";

    private bool _isReviving;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public string? FollowUpStateId { get; set; }

    public bool IsReviving => _isReviving;

    public override bool ShouldPowerBeRemovedOnDeath(CombatState cs, PowerModel power)
    {
        if (power.Type == PowerType.Debuff)
        {
            return power is not ITemporaryPower;
        }
        return false;
    }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        if (Owner.HasPower<MinionPower>())
        {
            return;
        }
        PowerCmd.Apply<MinionPower>(cs, Owner, 1, null, null);
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner) || creature is not MonsterState monster)
        {
            return;
        }
        _isReviving = true;
        string followUp = FollowUpStateId ?? LastLogged(monster);
        var state = new MoveStateDef(ReviveMoveId, ReviveMove, Intents.Heal)
        {
            FollowUpStateId = followUp,
            MustPerformOnceBeforeTransitioning = true
        };
        MonsterLifecycle.SetMoveImmediate(monster, state);
    }

    private static string LastLogged(MonsterState monster)
    {
        var log = monster.Ai.StateLog;
        if (log.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        return log[log.Count - 1];
    }

    private static void ReviveMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var power = self.GetPower<IllusionPower>();
        if (power != null)
        {
            power._isReviving = false;
        }
        CreatureCmd.Heal(cs, self, self.MaxHp - self.CurrentHp);
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

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isReviving);
        w.WriteBool(FollowUpStateId != null);
        w.WriteString(FollowUpStateId ?? string.Empty);
    }
}
