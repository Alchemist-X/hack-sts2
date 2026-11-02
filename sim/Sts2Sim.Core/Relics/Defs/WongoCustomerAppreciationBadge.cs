namespace Sts2Sim.Core.Relics;

public sealed class WongoCustomerAppreciationBadge : RelicModel
{
    public const string RelicId = "WONGO_CUSTOMER_APPRECIATION_BADGE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;
}
