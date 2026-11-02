namespace Sts2Sim.Core.Relics;

public sealed class TanxsWhistle : RelicModel
{
    public const string RelicId = "TANXS_WHISTLE";

    public const string WhistleCardId = "WHISTLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(WhistleCardId), Owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }
}
