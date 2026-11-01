namespace Sts2Sim.Core.Powers;

public sealed class IllusionPower : PowerModel
{
    public const string PowerId = "ILLUSION_POWER";

    public const string ReviveMoveId = "REVIVE_MOVE";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public string? FollowUpStateId { get; set; }

    public bool IsReviving { get; private set; }

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
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        PowerCmd.Apply<MinionPower>(cs, Owner, 1m, null, null);
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        IsReviving = true;
        var monster = (MonsterState)Owner;
        string? followUp = FollowUpStateId;
        if (followUp == null)
        {
            var log = monster.Ai.StateLog;
            if (log.Count == 0)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }
            followUp = log[log.Count - 1];
        }
        var state = new MoveStateDef(ReviveMoveId, ReviveMove, Intents.Heal)
        {
            FollowUpStateId = followUp,
            MustPerformOnceBeforeTransitioning = true
        };
        MonsterLifecycle.SetMoveImmediate(monster, state);
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

    private static void ReviveMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var power = self.GetPower<IllusionPower>() ?? throw new InvalidOperationException($"{PowerId}: REVIVE_MOVE ran on a monster without the power");
        power.IsReviving = false;
        CreatureCmd.Heal(cs, self, self.MaxHp - self.CurrentHp);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteString(FollowUpStateId);
        w.WriteBool(IsReviving);
    }
}
