namespace Sts2Sim.Core.Cards;

public sealed class BurningPact : CardDef
{
    public const string CardId = "BURNING_PACT";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;
    public const int SelectCount = 1;

    public BurningPact()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
