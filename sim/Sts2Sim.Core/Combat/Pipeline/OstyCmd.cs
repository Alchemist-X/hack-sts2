namespace Sts2Sim.Core.Combat.Pipeline;

public readonly record struct SummonResult(MonsterState? Creature, decimal Amount);

public static class OstyCmd
{
    public const string DieForYouPowerId = "DIE_FOR_YOU_POWER";
    public const decimal DieForYouAmount = 1m;

    public static SummonResult Summon(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(summoner);
        amount = Hook.ModifySummonAmount(cs, summoner, amount, source);
        if (amount == 0m)
        {
            return new SummonResult(PlayerPets.GetOsty(cs, summoner), 0m);
        }
        var osty = FirstOstyAmongAllies(cs, summoner);
        if (PlayerPets.IsOstyAlive(cs, summoner))
        {
            CreatureCmd.GainMaxHp(cs, PlayerPets.GetOsty(cs, summoner)!, amount);
        }
        else
        {
            bool isReviving = osty != null;
            if (osty != null)
            {
                if (osty.IsAlive)
                {
                    throw new InvalidOperationException("We shouldn't make it here if Osty is still alive!");
                }
                PlayerPets.AddPetInternal(summoner, osty);
            }
            else
            {
                osty = PlayerCmd.AddPet(cs, summoner, MonsterRegistry.Get(Osty.MonsterIdEntry));
                ApplyDieForYou(cs, osty);
            }
            CreatureCmd.SetMaxHp(cs, osty, amount);
            CreatureCmd.Heal(cs, osty, amount);
            if (isReviving)
            {
                Hook.AfterOstyRevived(cs, osty);
            }
        }
        cs.History.AppendSummoned(cs, (int)amount, summoner);
        Hook.AfterSummon(cs, summoner, amount);
        return new SummonResult(PlayerPets.GetOsty(cs, summoner), amount);
    }

    private static void ApplyDieForYou(CombatState cs, MonsterState osty)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return;
        }
        if (!CombatStatus.CanReceivePowers(cs, osty))
        {
            return;
        }
        var canonical = PowerRegistry.Create(DieForYouPowerId);
        var existing = PowerCmd.FindExistingInstanceForStacking(canonical, osty, null);
        if (existing == null)
        {
            PowerCmd.Apply(cs, canonical, osty, DieForYouAmount, null, null);
        }
        else
        {
            PowerCmd.ModifyAmount(cs, existing, DieForYouAmount, null, null);
        }
    }

    private static MonsterState? FirstOstyAmongAllies(CombatState cs, PlayerState summoner)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is MonsterState monster && monster.MonsterId == Osty.MonsterIdEntry && monster.PetOwnerId == summoner.CombatId)
            {
                return monster;
            }
        }
        return null;
    }
}

public static class PlayerPets
{
    public static void GetPets(CombatState cs, PlayerState player, List<MonsterState> buffer)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        var ids = player.PetIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var pet = FindPet(cs, ids[i]);
            if (pet != null)
            {
                buffer.Add(pet);
            }
        }
    }

    public static MonsterState? GetPet(CombatState cs, PlayerState player, string monsterId)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        var ids = player.PetIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var pet = FindPet(cs, ids[i]);
            if (pet != null && pet.MonsterId == monsterId)
            {
                return pet;
            }
        }
        return null;
    }

    public static MonsterState? GetOsty(CombatState cs, PlayerState player) => GetPet(cs, player, Osty.MonsterIdEntry);

    public static bool IsOstyAlive(CombatState cs, PlayerState player) => GetOsty(cs, player)?.IsAlive ?? false;

    public static bool IsOstyMissing(CombatState cs, PlayerState player) => !IsOstyAlive(cs, player);

    public static void AddPetInternal(PlayerState player, MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pet);
        if (player.PetIds.Contains(pet.CombatId))
        {
            return;
        }
        if (pet.PetOwnerId != player.CombatId)
        {
            if (pet.PetOwnerId != PowerModel.NoCreature)
            {
                throw new InvalidOperationException($"Pet {pet.MonsterId} (combat id {pet.CombatId}) already has an owner.");
            }
            pet.PetOwnerId = player.CombatId;
        }
        player.PetIds.Add(pet.CombatId);
    }

    public static void OnPetDied(CombatState cs, PlayerState player, MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pet);
        if (!player.PetIds.Contains(pet.CombatId))
        {
            throw new InvalidOperationException("Player does not have pet " + pet.MonsterId);
        }
        if (Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet))
        {
            player.PetIds.Remove(pet.CombatId);
        }
    }

    public static void NotifyPetDied(CombatState cs, CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(creature);
        if (creature is not MonsterState pet)
        {
            return;
        }
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.PetIds.Contains(pet.CombatId))
            {
                OnPetDied(cs, player, pet);
            }
        }
    }

    public static void ResetForNewCombat(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.PetIds.Clear();
    }

    private static MonsterState? FindPet(CombatState cs, int combatId)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is MonsterState monster && monster.CombatId == combatId)
            {
                return monster;
            }
        }
        return cs.GetCreatureById(combatId) as MonsterState;
    }
}
