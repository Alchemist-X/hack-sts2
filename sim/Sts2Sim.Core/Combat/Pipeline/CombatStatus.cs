namespace Sts2Sim.Core.Combat.Pipeline;

public static class CombatStatus
{
    public static bool IsInProgress(CombatState cs) => cs.IsInProgress;

    public static bool IsStarting(CombatState cs) => cs.IsStarting;

    public static bool IsAboutToLose(CombatState cs) => cs.IsInProgress && cs.Outcome == CombatOutcome.Defeat;

    public static bool IsEnding(CombatState cs)
    {
        if (!cs.IsInProgress)
        {
            return false;
        }
        if (cs.Outcome == CombatOutcome.Defeat)
        {
            return true;
        }
        if (AnyPrimaryEnemyAlive(cs))
        {
            return false;
        }
        return !Hook.ShouldStopCombatFromEnding(cs);
    }

    public static bool IsOverOrEnding(CombatState cs)
    {
        if (!IsEnding(cs))
        {
            return !cs.IsInProgress;
        }
        return true;
    }

    public static bool AnyPrimaryEnemyAlive(CombatState cs)
    {
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m.IsAlive && IsPrimaryEnemy(m))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsSecondaryEnemy(CreatureState creature)
    {
        if (creature.Side != CombatSide.Enemy)
        {
            return false;
        }
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (powers[i].OwnerIsSecondaryEnemy)
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsPrimaryEnemy(CreatureState creature)
    {
        if (creature.Side != CombatSide.Enemy)
        {
            return false;
        }
        return !IsSecondaryEnemy(creature);
    }

    public static bool IsAttached(CombatState cs, CreatureState creature)
    {
        if (ReferenceEquals(creature, cs.Player))
        {
            return true;
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (ReferenceEquals(monsters[i], creature))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsActiveForHooks(CombatState cs, PlayerState player) => player.IsActiveForHooks;

    public static bool IsHittable(CombatState cs, CreatureState creature)
    {
        if (creature.IsDead)
        {
            return false;
        }
        return Hook.ShouldAllowHitting(cs, creature);
    }

    public static bool CanReceivePowers(CombatState cs, CreatureState creature)
    {
        if (!IsAttached(cs, creature))
        {
            return false;
        }
        return Hook.ShouldAllowHitting(cs, creature);
    }

    public static CreatureState CardOwnerCreature(CombatState cs, CardInstance card) => cs.Player;

    public static void GetCreaturesOnSide(CombatState cs, CombatSide side, List<CreatureState> buffer)
    {
        buffer.Clear();
        if (side != CombatSide.Enemy)
        {
            buffer.Add(cs.Player);
            return;
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            buffer.Add(monsters[i]);
        }
    }

    public static void GetOpponentsOf(CombatState cs, CreatureState creature, List<CreatureState> buffer)
    {
        GetCreaturesOnSide(cs, OppositeSide(creature.Side), buffer);
    }

    public static void GetTeammatesOf(CombatState cs, CreatureState creature, List<CreatureState> buffer)
    {
        GetCreaturesOnSide(cs, creature.Side, buffer);
    }

    public static CombatSide OppositeSide(CombatSide side) => side switch
    {
        CombatSide.None => CombatSide.None,
        CombatSide.Player => CombatSide.Enemy,
        CombatSide.Enemy => CombatSide.Player,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
    };

    public static void LoseCombat(CombatState cs)
    {
        if (cs.IsInProgress && cs.Outcome == CombatOutcome.InProgress)
        {
            cs.Outcome = CombatOutcome.Defeat;
        }
    }
}
