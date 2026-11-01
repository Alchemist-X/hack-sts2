namespace Sts2Sim.Core.Monsters;

public static class MonsterLifecycle
{
    public static void SetUpForCombat(CombatState cs, MonsterState monster)
    {
        monster.InitAi(new MonsterAiRuntime(monster.Def.GenerateMoveStateMachine(cs, monster)));
        monster.SpawnedThisTurn = true;
    }

    public static void AfterCreatureAdded(CombatState cs, MonsterState monster)
    {
        if (monster.Side == CombatSide.Enemy)
        {
            monster.Def.AfterAddedToRoom(cs, monster);
        }
        if (monster.IsEnemy && cs.CurrentSide == CombatSide.Player)
        {
            RollMove(cs, monster);
        }
    }

    public static MoveStateDef RollMove(CombatState cs, MonsterState monster)
        => monster.Ai.RollMove(cs, monster, cs.Rng.MonsterAi);

    public static void PrepareForNextTurn(CombatState cs, MonsterState monster, bool rollNewMove = true)
    {
        if (rollNewMove && monster.HasAi)
        {
            RollMove(cs, monster);
        }
    }

    public static void TakeTurn(CombatState cs, MonsterState monster)
    {
        if (monster.Side != CombatSide.Enemy)
        {
            throw new InvalidOperationException("Only enemy monsters can take automated turns.");
        }
        if (!monster.SpawnedThisTurn)
        {
            PerformMove(cs, monster);
        }
    }

    public static void PerformMove(CombatState cs, MonsterState monster)
    {
        var ai = monster.Ai;
        ai.IsPerformingMove = true;
        var move = ai.NextMove ?? throw new InvalidOperationException("No move has been set for the monster");
        var targets = PlayerCreatures(cs);
        ai.MarkPerformedAtLeastOnce(move);
        move.Perform(cs, monster, targets);
        ai.OnMovePerformed(move);
        ai.IsPerformingMove = false;
        if (monster.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, monster))
        {
            cs.Monsters.Remove(monster);
        }
    }

    public static void OnSideSwitch(MonsterState monster)
    {
        monster.SpawnedThisTurn = false;
    }

    public static void SetMoveImmediate(MonsterState monster, MoveStateDef state, bool forceTransition = false)
        => monster.Ai.SetMoveImmediate(state, forceTransition);

    public static void StunInternal(CombatState cs, MonsterState monster, MovePerform stunMove, string? nextMoveId)
    {
        if (!cs.Monsters.Contains(monster) || monster.IsDead)
        {
            return;
        }
        var ai = monster.Ai;
        if (string.IsNullOrEmpty(nextMoveId))
        {
            var log = ai.StateLog;
            if (log.Count == 0)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }
            nextMoveId = log[log.Count - 1];
        }
        var state = new MoveStateDef(MoveStateDef.StunnedMoveId, stunMove, Intents.Stun)
        {
            FollowUpStateId = nextMoveId,
            MustPerformOnceBeforeTransitioning = true
        };
        ai.SetMoveImmediate(state);
    }

    public static bool IntendsToAttack(MonsterState monster) => monster.HasAi && monster.Ai.IntendsToAttack;

    public static IReadOnlyList<CreatureState> PlayerCreatures(CombatState cs) => new CreatureState[] { cs.Player };

    public static IReadOnlyList<CreatureState> PlayerSideParticipants(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var extraTurn = cs.PlayersTakingExtraTurn;
        if (extraTurn.Count > 0)
        {
            var players = new CreatureState[extraTurn.Count];
            for (int i = 0; i < players.Length; i++)
            {
                players[i] = FindPlayer(cs, extraTurn[i]);
            }
            return players;
        }
        return Snapshot(cs.Allies);
    }

    public static IReadOnlyList<CreatureState> EnemySideParticipants(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        return Snapshot(cs.Monsters);
    }

    private static CreatureState FindPlayer(CombatState cs, int playerCombatId)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.CombatId == playerCombatId)
            {
                return player;
            }
        }
        throw new InvalidOperationException($"Player {playerCombatId} is taking an extra turn but is not in this combat.");
    }

    private static CreatureState[] Snapshot<T>(IReadOnlyList<T> side) where T : CreatureState
    {
        int n = side.Count;
        if (n == 0)
        {
            return Array.Empty<CreatureState>();
        }
        var copy = new CreatureState[n];
        for (int i = 0; i < n; i++)
        {
            copy[i] = side[i];
        }
        return copy;
    }
}
