namespace Sts2Sim.Core.Relics;

public sealed class LeadPaperweight : RelicModel
{
    public const string RelicId = "LEAD_PAPERWEIGHT";

    public const int CardCount = 2;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        var options = new CardCreationOptions(new[] { CardPools.Colorless }, CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        var cardOptions = CardFactory.CreateForReward(rs, Owner, CardCount, options).Select(c => c.Card).ToList();
        var chosenCard = RelicDeckPromptsR91.FromChooseACardScreen(rs, cardOptions, true, RelicId);
        if (chosenCard != null)
        {
            CardPileCmd.Add(rs, chosenCard, PileType.Deck);
        }
    }
}
