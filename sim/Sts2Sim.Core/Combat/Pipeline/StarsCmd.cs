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
        => throw new NotImplementedException("StarsCmd.SpendStars: character-mechanics (Models/CardModel.cs:3435-3446, state machine :1399-1466; AfterStarsSpent :1436)");
}
