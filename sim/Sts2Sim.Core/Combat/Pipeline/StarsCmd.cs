namespace Sts2Sim.Core.Combat.Pipeline;

public static class StarsCmd
{
    public static void GainStars(CombatState cs, PlayerState player, decimal amount)
        => throw new NotImplementedException("StarsCmd.GainStars: character-mechanics (Commands/PlayerCmd.cs:844, state machine :358-423; gate :390, AfterStarsGained((int)amount) :393)");

    public static void LoseStars(CombatState cs, PlayerState player, decimal amount)
        => throw new NotImplementedException("StarsCmd.LoseStars: character-mechanics (Commands/PlayerCmd.cs:857-866)");

    public static void SetStars(CombatState cs, PlayerState player, decimal amount)
        => throw new NotImplementedException("StarsCmd.SetStars: character-mechanics (Commands/PlayerCmd.cs:868, state machine :695-793; gate :742)");

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

    private static void LoseStarsInternal(CombatState cs, PlayerState player, decimal amount)
    {
        int before = player.Stars;
        player.LoseStars(amount);
        int after = player.Stars;
        if (after != before)
        {
            cs.History.AppendStarsModified(cs, after - before, player);
        }
    }
}
