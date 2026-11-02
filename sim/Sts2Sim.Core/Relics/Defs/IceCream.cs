namespace Sts2Sim.Core.Relics;

public sealed class IceCream : RelicModel
{
    public const string RelicId = "ICE_CREAM";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool ShouldPlayerResetEnergy(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        if (Owner.TurnNumber == 1)
        {
            return true;
        }
        return false;
    }
}
