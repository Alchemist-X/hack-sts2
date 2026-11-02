namespace Sts2Sim.Core.Monsters;

public static class MonsterLifecycle
{
    public static void SetUpForCombat(CombatState cs, MonsterState monster)
    {
        monster.InitAi(new MonsterAiRuntime(monster.Def.GenerateMoveStateMachine(cs, monster)));
        monster.SpawnedThisTurn = true;
        monster.Listener = new MonsterListener(monster);
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
        if (rollNewMove && HasMoveStateMachine(monster))
        {
            RollMove(cs, monster);
        }
    }

    public static bool HasMoveStateMachine(MonsterState monster) => monster.HasAi && !monster.Ai.IsMachineReset;

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
        var move = ai.NextMove;
        var targets = PlayerCreatures(cs);
        if (move == null)
        {
            ai.PerformedAtLeastOnce.Add(MoveStateDef.UnsetMoveId);
            throw new InvalidOperationException("No move has been set for the monster");
        }
        ai.MarkPerformedAtLeastOnce(move);
        move.Perform(cs, monster, targets);
        if (!ai.IsMachineReset)
        {
            ai.OnMovePerformed(move);
        }
        cs.History.AppendMonsterPerformedMove(cs, monster, move, targets);
        ai.IsPerformingMove = false;
        if (monster.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, monster))
        {
            RemoveFromCombatState(cs, monster);
        }
    }

    public static void RemoveCreature(CombatState cs, MonsterState monster)
    {
        monster.Def.BeforeRemovedFromRoom(cs, monster);
        ResetStateMachine(monster);
    }

    public static void ResetStateMachine(MonsterState monster)
    {
        if (monster.HasAi)
        {
            monster.Ai.ResetMachine();
        }
    }

    public static void RemoveFromCombatState(CombatState cs, MonsterState monster)
    {
        int index = MonsterSpawner.IndexOfReference(cs.Monsters, monster);
        if (index >= 0)
        {
            cs.Monsters.RemoveAt(index);
            return;
        }
        if (MonsterSpawner.IndexOfReference(cs.Allies, monster) >= 0)
        {
            cs.RemoveAlly(monster);
        }
    }

    public static void InvokeDiedEvent(CombatState cs, CreatureState creature)
    {
        if (creature is not MonsterState pet || pet.PetOwnerId == PowerModel.NoCreature)
        {
            return;
        }
        var owner = FindPlayerOrNull(cs, pet.PetOwnerId);
        if (owner != null && owner.PetIds.Contains(pet.CombatId))
        {
            OnPetDied(cs, owner, pet);
        }
    }

    public static void OnPetDied(CombatState cs, PlayerState owner, MonsterState pet)
    {
        if (!owner.PetIds.Contains(pet.CombatId))
        {
            throw new InvalidOperationException("Player does not have pet " + pet.MonsterId);
        }
        if (Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet))
        {
            owner.PetIds.Remove(pet.CombatId);
        }
    }

    public static MonsterState? GetPet(CombatState cs, PlayerState owner, string monsterId)
    {
        var pets = owner.PetIds;
        for (int i = 0; i < pets.Count; i++)
        {
            var pet = FindAllyMonster(cs, pets[i]);
            if (pet != null && string.Equals(pet.MonsterId, monsterId, StringComparison.Ordinal))
            {
                return pet;
            }
        }
        return null;
    }

    public static void GetPets(CombatState cs, PlayerState owner, List<MonsterState> buffer)
    {
        buffer.Clear();
        var pets = owner.PetIds;
        for (int i = 0; i < pets.Count; i++)
        {
            var pet = FindAllyMonster(cs, pets[i]);
            if (pet != null)
            {
                buffer.Add(pet);
            }
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
        if (!IsInCombat(cs, monster) || monster.IsDead)
        {
            return;
        }
        var ai = monster.Ai;
        if (string.IsNullOrEmpty(nextMoveId))
        {
            ai.RequireMachine();
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

    public static bool IsInCombat(CombatState cs, MonsterState monster)
        => MonsterSpawner.IndexOfReference(cs.Monsters, monster) >= 0 || MonsterSpawner.IndexOfReference(cs.Allies, monster) >= 0;

    private static CreatureState FindPlayer(CombatState cs, int playerCombatId)
        => FindPlayerOrNull(cs, playerCombatId)
           ?? throw new InvalidOperationException($"Player {playerCombatId} is taking an extra turn but is not in this combat.");

    private static PlayerState? FindPlayerOrNull(CombatState cs, int playerCombatId)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.CombatId == playerCombatId)
            {
                return player;
            }
        }
        return null;
    }

    private static MonsterState? FindAllyMonster(CombatState cs, int combatId)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is MonsterState monster && monster.CombatId == combatId)
            {
                return monster;
            }
        }
        return null;
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
