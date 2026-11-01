namespace Sts2Sim.Core.Combat.Loop;

public static class PlayerCmd
{
    public static void GainEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.GainEnergy(cs, amount, player);

    public static void LoseEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.LoseEnergy(cs, amount, player);

    public static void SetEnergy(CombatState cs, decimal amount, PlayerState player) => EnergyCmd.SetEnergy(cs, amount, player);

    public static MonsterState AddPet(CombatState cs, PlayerState owner, MonsterDef def)
        => throw new NotImplementedException("PlayerCmd.AddPet: loop (Commands/PlayerCmd.cs:946-967)");
}
