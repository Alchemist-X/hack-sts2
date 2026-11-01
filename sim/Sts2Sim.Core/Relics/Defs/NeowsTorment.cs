namespace Sts2Sim.Core.Relics;

public sealed class NeowsTorment : RelicModel
{
    public const string RelicId = "NEOWS_TORMENT";

    public const string NeowsFuryCardId = "NEOWS_FURY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(NeowsFuryCardId), Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
