namespace Sts2Sim.Core.Relics;

public sealed class PaelsHorn : RelicModel
{
    public const string RelicId = "PAELS_HORN";

    public const string RelaxCardId = "RELAX";

    public const int RelaxCount = 2;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        for (int i = 0; i < RelaxCount; i++)
        {
            var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(RelaxCardId), Owner);
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
    }
}
