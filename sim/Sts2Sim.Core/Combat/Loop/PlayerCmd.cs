namespace Sts2Sim.Core.Combat.Loop;

public static class PlayerCmd
{
    public static void GainEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.GainEnergy(cs, amount, player);

    public static void LoseEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.LoseEnergy(cs, amount, player);

    public static void SetEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.SetEnergy(cs, amount, player);

    public static void GainStars(CombatState cs, decimal amount, PlayerState player) => StarsCmd.GainStars(cs, player, amount);

    public static void LoseStars(CombatState cs, decimal amount, PlayerState player) => StarsCmd.LoseStars(cs, player, amount);

    public static void SetStars(CombatState cs, decimal amount, PlayerState player) => StarsCmd.SetStars(cs, player, amount);

    public static void GainGold(CombatState cs, decimal amount, PlayerState player, bool wasStolenBack = false)
        => GainGold(cs.Run, amount, player, wasStolenBack);

    public static void GainGold(RunState rs, decimal amount, PlayerState player, bool wasStolenBack = false)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        var cs = rs.Combat;
        List<AbstractModel>? modifiers = null;
        amount = Hook.ModifyGoldGained(rs, cs, amount, player, ref modifiers);
        Hook.AfterModifyingGoldGained(rs, cs, modifiers, player, amount);
        if (amount > 0m)
        {
            player.Gold += (int)amount;
            Hook.AfterGoldGained(rs, player);
        }
    }

    public static void LoseGold(CombatState cs, decimal amount, PlayerState player, GoldLossType goldLossType = GoldLossType.Lost)
        => LoseGold(cs.Run, amount, player, goldLossType);

    public static void LoseGold(RunState rs, decimal amount, PlayerState player, GoldLossType goldLossType = GoldLossType.Lost)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        player.Gold = int.Max(0, player.Gold - (int)amount);
    }

    public static void SetGold(CombatState cs, decimal amount, PlayerState player) => SetGold(cs.Run, amount, player);

    public static void SetGold(RunState rs, decimal amount, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        int gold = player.Gold;
        if (gold < amount)
        {
            GainGold(rs, amount - gold, player);
        }
        else if (gold > amount)
        {
            LoseGold(rs, gold - amount, player);
        }
    }

    public static void GainMaxPotionCount(RunState rs, int amount, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.AddToMaxPotionCount(amount);
    }

    public static void LoseMaxPotionCount(RunState rs, int amount, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.SubtractFromMaxPotionCount(amount);
    }

    public static MonsterState AddPet(CombatState cs, PlayerState owner, string monsterId) => AddPet(cs, owner, MonsterRegistry.Get(monsterId));

    public static MonsterState AddPet(CombatState cs, PlayerState owner, MonsterDef def)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(def);
        var pet = MonsterSpawner.CreateCreature(cs, def, owner.Side, null);
        AddPet(cs, pet, owner);
        return pet;
    }

    public static void AddPet(CombatState cs, MonsterState pet, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(player);
        AddPetInternal(player, pet);
        CreatureCmd.Add(cs, pet);
    }

    public static void MimicRestSiteHeal(RunState rs, PlayerState player)
        => HealRestSiteOption.ExecuteRestSiteHeal(rs, player, true);

    public static void EndTurn(CombatState cs, PlayerState player, bool canBackOut)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (!IsPlayerReadyToEndTurn(cs, player))
        {
            SetReadyToEndTurn(cs, player, canBackOut);
        }
    }

    public static bool IsPlayerReadyToEndTurn(CombatState cs, PlayerState player)
        => cs.IsInProgress && cs.IsPlayerReadyToEndTurn(player);

    public static void SetReadyToEndTurn(CombatState cs, PlayerState player, bool canBackOut)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (!cs.IsInProgress)
        {
            return;
        }
        var ready = cs.PlayersReadyToEndTurn;
        if (ready.Contains(player.CombatId))
        {
            return;
        }
        ready.Add(player.CombatId);
    }

    public static void UndoReadyToEndTurn(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (cs.IsInProgress)
        {
            cs.PlayersReadyToEndTurn.Remove(player.CombatId);
        }
    }

    public static void CompleteQuest(RunState rs, CardInstance questCard)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(questCard);
    }

    private static void AddPetInternal(PlayerState player, MonsterState pet)
    {
        var pets = player.PetIds;
        if (pets.Contains(pet.CombatId))
        {
            return;
        }
        if (pet.PetOwnerId != player.CombatId)
        {
            if (pet.PetOwnerId != PowerModel.NoCreature)
            {
                throw new InvalidOperationException($"Pet {pet.MonsterId}#{pet.CombatId} already has an owner.");
            }
            pet.PetOwnerId = player.CombatId;
        }
        pets.Add(pet.CombatId);
    }
}
