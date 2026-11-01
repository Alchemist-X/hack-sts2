namespace Sts2Sim.Core.Relics;

public sealed class DollysMirror : RelicModel
{
    public const string RelicId = "DOLLYS_MIRROR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(RelicId + ".selectionScreenPrompt", 1);
        var result = RelicDeckPromptsR91.FromDeckGeneric(rs, Owner, prefs, RelicId, Filter);
        var cardModel = result.FirstOrDefault();
        if (cardModel != null)
        {
            var card = RelicDeckPromptsR91.CloneCard(rs, cardModel);
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
    }

    private static bool Filter(CardInstance c)
    {
        return c.Type != CardType.Quest;
    }
}
