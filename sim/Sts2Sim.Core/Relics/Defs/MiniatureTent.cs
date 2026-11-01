namespace Sts2Sim.Core.Relics;

public sealed class MiniatureTent : RelicModel
{
    public const string RelicId = "MINIATURE_TENT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return false;
    }
}
