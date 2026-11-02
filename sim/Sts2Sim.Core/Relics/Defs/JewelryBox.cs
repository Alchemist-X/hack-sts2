namespace Sts2Sim.Core.Relics;

public sealed class JewelryBox : RelicModel
{
    public const string RelicId = "JEWELRY_BOX";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public const string ApotheosisCardId = "APOTHEOSIS";

    public override void AfterObtained(RunState rs)
    {
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(ApotheosisCardId), Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
