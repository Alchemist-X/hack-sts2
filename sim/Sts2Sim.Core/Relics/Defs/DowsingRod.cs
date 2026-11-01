namespace Sts2Sim.Core.Relics;

public sealed class DowsingRod : RelicModel
{
    public const string RelicId = "DOWSING_ROD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public const string DowsingCardId = "DOWSING";

    public const string UnportedCardSite = "DowsingRod.AfterObtained: card DOWSING is not registered; nothing added";

    public override void AfterObtained(RunState rs)
    {
        if (!CardRegistry.TryGet(DowsingCardId, out var def))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedCardSite);
            return;
        }
        var card = CardFactory.CreateRunCard(rs, def, Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
