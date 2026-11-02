namespace Sts2Sim.Core.Cards;

public sealed class Prepared : CardDef
{
    public const string CardId = "PREPARED";
    public const int Cost = 0;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;

    public Prepared()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
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
        int cardCount = card.Vars.Cards.IntValue;
        CardPileCmd.Draw(cs, cardCount, owner);
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, cardCount);
        var cards = CardSelectCmd.FromHandForDiscard(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        CardCmd.Discard(cs, cards);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
