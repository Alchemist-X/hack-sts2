namespace Sts2Sim.Core.Combat.Loop;

public static class PlayerCmd
{
    public static void GainEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.GainEnergy(cs, amount, player);

    public static void LoseEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.LoseEnergy(cs, amount, player);

    public static void SetEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.SetEnergy(cs, amount, player);
}
