namespace Sts2Sim.Core.Combat.Loop;

public static class PotionActions
{
    public static bool CanUse(CombatState cs, int slot, CreatureState? target)
        => throw new NotImplementedException("PotionActions.CanUse: relics-potions (GameActions/UsePotionAction.cs:96-122, CONTRACTS Part B §B1)");

    public static void UsePotion(CombatState cs, int slot, CreatureState? target)
        => throw new NotImplementedException("PotionActions.UsePotion: relics-potions (GameActions/UsePotionAction.cs, Models/PotionModel.cs:656)");

    public static void DiscardPotion(CombatState cs, int slot)
        => throw new NotImplementedException("PotionActions.DiscardPotion: relics-potions (Commands/PotionCmd.cs:283, Nodes.Potions/NPotionPopup.cs:285-345)");
}
