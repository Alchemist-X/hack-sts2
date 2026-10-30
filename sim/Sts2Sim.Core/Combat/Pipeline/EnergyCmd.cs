namespace Sts2Sim.Core.Combat.Pipeline;

public static class EnergyCmd
{
    public const decimal MaxEnergyValue = 999999999m;

    public static int GetMaxEnergy(CombatState cs, PlayerState player) => (int)Hook.ModifyMaxEnergy(cs, player, player.BaseMaxEnergy);

    public static void ResetEnergy(CombatState cs, PlayerState player)
    {
        player.Energy = GetMaxEnergy(cs, player);
    }

    public static void AddMaxEnergyToCurrent(CombatState cs, PlayerState player)
    {
        player.Energy += GetMaxEnergy(cs, player);
    }

    public static void GainEnergyInternal(PlayerState player, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("Must not be negative.", nameof(amount));
        }
        player.Energy = (int)Math.Clamp(player.Energy + amount, 0m, MaxEnergyValue);
    }

    public static void LoseEnergyInternal(PlayerState player, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("Must not be negative.", nameof(amount));
        }
        player.Energy = (int)Math.Clamp(player.Energy - amount, 0m, MaxEnergyValue);
    }

    public static void GainEnergy(CombatState cs, decimal amount, PlayerState player)
    {
        if (amount <= 0m || CombatStatus.IsEnding(cs))
        {
            return;
        }
        List<AbstractModel>? modifiers = null;
        decimal finalAmount = Hook.ModifyEnergyGain(cs, player, amount, ref modifiers);
        Hook.AfterModifyingEnergyGain(cs, modifiers);
        if (finalAmount > 0m)
        {
            GainEnergyInternal(player, finalAmount);
        }
    }

    public static void LoseEnergy(CombatState cs, decimal amount, PlayerState player)
    {
        if (amount <= 0m)
        {
            return;
        }
        if (CombatStatus.IsEnding(cs))
        {
            return;
        }
        LoseEnergyInternal(player, amount);
    }

    public static void SetEnergy(CombatState cs, decimal amount, PlayerState player)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return;
        }
        int energy = player.Energy;
        if (energy < amount)
        {
            GainEnergy(cs, amount - energy, player);
        }
        else if (energy > amount)
        {
            LoseEnergy(cs, energy - amount, player);
        }
    }

    public static void SpendEnergy(CombatState cs, CardInstance card, int amount)
    {
        if (card.EnergyCost.CostsX)
        {
            card.EnergyCost.CapturedXValue = amount;
        }
        if (amount > 0)
        {
            LoseEnergyInternal(cs.Player, Math.Max(0, amount));
        }
        Hook.AfterEnergySpent(cs, card, amount);
    }
}
