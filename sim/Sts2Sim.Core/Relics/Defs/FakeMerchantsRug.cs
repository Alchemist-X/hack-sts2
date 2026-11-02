namespace Sts2Sim.Core.Relics;

public sealed class FakeMerchantsRug : RelicModel
{
    public const string RelicId = "FAKE_MERCHANTS_RUG";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;
}
