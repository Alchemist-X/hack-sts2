namespace Sts2Sim.Core.Relics;

public sealed class NeowsSacrifice : RelicModel
{
    public const string RelicId = "NEOWS_SACRIFICE";

    public const string AmbergrisPotionId = "AMBERGRIS";

    public const string GuiltyCardId = "GUILTY";

    public const string UnportedGuiltySite = "NeowsSacrifice.AfterObtained: card GUILTY is not registered; deck add skipped";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        PotionCmd.TryToProcure(rs, AmbergrisPotionId, Owner);
        if (!CardRegistry.TryGet(GuiltyCardId, out var guilty))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedGuiltySite);
            return;
        }
        var card = CardFactory.CreateRunCard(rs, guilty, Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
