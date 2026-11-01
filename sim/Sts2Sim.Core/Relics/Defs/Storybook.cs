namespace Sts2Sim.Core.Relics;

public sealed class Storybook : RelicModel
{
    public const string RelicId = "STORYBOOK";

    public const string BrightestFlameCardId = "BRIGHTEST_FLAME";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(BrightestFlameCardId), Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
