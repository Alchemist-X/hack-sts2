namespace Sts2Sim.Core.Combat.Pipeline;

public static class StarsCmd
{
    public const int StarsPerMissingEnergy = 2;

    public static void GainStars(CombatState cs, PlayerState player, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (!CombatStatus.IsEnding(cs) && Hook.ShouldGainStars(cs, amount, player))
        {
            GainStarsInternal(cs, player, amount);
            Hook.AfterStarsGained(cs, (int)amount, player);
        }
    }

    public static void LoseStars(CombatState cs, PlayerState player, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsEnding(cs))
        {
            return;
        }
        LoseStarsInternal(cs, player, amount);
    }

    public static void SetStars(CombatState cs, PlayerState player, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsEnding(cs))
        {
            return;
        }
        int stars = player.Stars;
        if (stars < amount)
        {
            GainStars(cs, player, amount - stars);
        }
        else if (stars > amount)
        {
            LoseStars(cs, player, stars - amount);
        }
    }

    public static void SpendStars(CombatState cs, PlayerState player, int amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (amount > 0)
        {
            LoseStarsInternal(cs, player, amount);
            Hook.AfterStarsSpent(cs, amount, player);
        }
    }

    public static void PayExcessEnergyCostWithStars(CombatState cs, PlayerState player, ref int energyCost, ref int starCost)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        int energy = player.Energy;
        if (energyCost > energy && Hook.ShouldPayExcessEnergyCostWithStars(cs, player))
        {
            starCost += (energyCost - energy) * StarsPerMissingEnergy;
            energyCost = energy;
        }
    }

    public static void ResetForNewCombat(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.Stars = 0;
    }

    private static void GainStarsInternal(CombatState cs, PlayerState player, decimal amount)
    {
        int before = player.Stars;
        player.GainStars(amount);
        RecordStarsModified(cs, player, before);
    }

    private static void LoseStarsInternal(CombatState cs, PlayerState player, decimal amount)
    {
        int before = player.Stars;
        player.LoseStars(amount);
        RecordStarsModified(cs, player, before);
    }

    private static void RecordStarsModified(CombatState cs, PlayerState player, int before)
    {
        int after = player.Stars;
        if (after != before)
        {
            cs.History.AppendStarsModified(cs, after - before, player);
        }
    }
}
