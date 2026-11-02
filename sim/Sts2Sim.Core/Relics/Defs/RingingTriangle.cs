namespace Sts2Sim.Core.Relics;

public sealed class RingingTriangle : RelicModel
{
    public const string RelicId = "RINGING_TRIANGLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool ShouldFlush(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return Owner.TurnNumber > 1;
    }
}
