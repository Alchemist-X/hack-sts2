namespace Sts2Sim.Core.Relics;

public sealed class UnceasingTop : RelicModel
{
    public const string RelicId = "UNCEASING_TOP";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterHandEmptied(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || !IsValidPhase(player.Phase))
        {
            return;
        }
        CardPileCmd.Draw(cs, player);
    }

    private static bool IsValidPhase(PlayerTurnPhase phase)
    {
        if ((uint)(phase - 2) <= 2u)
        {
            return true;
        }
        return false;
    }
}
