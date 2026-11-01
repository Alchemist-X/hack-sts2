namespace Sts2Sim.Core.Combat.Pipeline;

public static class CombatStatus
{
    public const string OstyMonsterId = "OSTY";

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
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], creature))
            {
                return true;
            }
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

    public static CombatState? CombatOf(CombatState cs, CreatureState creature) => IsAttached(cs, creature) ? cs : null;

    public static CreatureState? FindCreature(CombatState cs, int combatId)
    {
        if (combatId == PowerModel.NoCreature)
        {
            return null;
        }
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i].CombatId == combatId)
            {
                return allies[i];
            }
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (monsters[i].CombatId == combatId)
            {
                return monsters[i];
            }
        }
        return null;
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

    public static CreatureState CardOwnerCreature(CombatState cs, CardInstance card) => card.HasOwner ? card.Owner : cs.Player;

    public static PlayerState CardOwner(CombatState cs, CardInstance card) => card.HasOwner ? card.Owner : cs.Player;

    public static PlayerState? PetOwnerOf(CombatState cs, CreatureState creature)
    {
        if (creature is not MonsterState pet || pet.PetOwnerId == PowerModel.NoCreature)
        {
            return null;
        }
        if (cs.Player.CombatId == pet.PetOwnerId)
        {
            return cs.Player;
        }
        return FindCreature(cs, pet.PetOwnerId) as PlayerState;
    }

    public static bool IsPet(CombatState cs, CreatureState creature) => PetOwnerOf(cs, creature) != null;

    public static MonsterState? GetPet(CombatState cs, PlayerState player, string monsterId)
    {
        var petIds = player.PetIds;
        for (int i = 0; i < petIds.Count; i++)
        {
            if (FindCreature(cs, petIds[i]) is MonsterState pet && string.Equals(pet.MonsterId, monsterId, StringComparison.Ordinal))
            {
                return pet;
            }
        }
        return null;
    }

    public static MonsterState? GetOsty(CombatState cs, PlayerState player) => GetPet(cs, player, OstyMonsterId);

    public static bool IsOstyAlive(CombatState cs, PlayerState player) => GetOsty(cs, player)?.IsAlive ?? false;

    public static bool IsOstyMissing(CombatState cs, PlayerState player) => !IsOstyAlive(cs, player);

    public static int PlayerCount(CombatState cs)
    {
        int n = 0;
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i].IsPlayer)
            {
                n++;
            }
        }
        return n;
    }

    public static bool AllPlayersDead(CombatState cs)
    {
        var players = cs.Run.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].IsAlive)
            {
                return false;
            }
        }
        return true;
    }

    public static bool AllCombatPlayersDead(CombatState cs)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i].IsPlayer && allies[i].IsAlive)
            {
                return false;
            }
        }
        return true;
    }

    public static void GetPlayerCreatures(CombatState cs, List<CreatureState> buffer)
    {
        buffer.Clear();
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i].IsPlayer)
            {
                buffer.Add(allies[i]);
            }
        }
    }

    public static void GetCreaturesOnSide(CombatState cs, CombatSide side, List<CreatureState> buffer)
    {
        buffer.Clear();
        if (side != CombatSide.Enemy)
        {
            var allies = cs.Allies;
            for (int i = 0; i < allies.Count; i++)
            {
                buffer.Add(allies[i]);
            }
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

    public static bool ContainsById(IReadOnlyList<CreatureState> participants, CreatureState creature)
    {
        int id = creature.CombatId;
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i].CombatId == id)
            {
                return true;
            }
        }
        return false;
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
