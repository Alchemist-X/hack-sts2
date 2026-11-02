namespace Sts2Sim.Core.Relics;

public sealed class HeftyTablet : RelicModel
{
    public const string RelicId = "HEFTY_TABLET";

    public const string InjuryCardId = "INJURY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var options = new CardCreationOptions(new[] { CardPools.ForCharacter(Owner.CharacterId) }, CardCreationSource.Other, CardRarityOddsType.Uniform, c => c.Rarity == CardRarity.Rare).WithFlags(CardCreationFlags.NoUpgradeRoll);
        var cardOptions = CardFactory.CreateForReward(rs, Owner, DynamicVars.Cards.IntValue, options).Select(r => r.Card).ToList();
        var chosenCard = RelicDeckPromptsR91.FromChooseACardScreen(rs, cardOptions, true, RelicId);
        var val3 = new List<CardInstance>(1)
        {
            CardFactory.CreateRunCard(rs, CardRegistry.Get(InjuryCardId), Owner)
        };
        if (chosenCard != null)
        {
            val3.Insert(0, chosenCard);
        }
        CardPileCmd.Add(rs, val3, PileType.Deck);
    }
}
