namespace Sts2Sim.Core.Cards;

public sealed class Acrobatics : CardDef
{
    public const string CardId = "ACROBATICS";
    public const int Cost = 1;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;
    public const int DiscardCount = 1;

    public Acrobatics()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var acrobatics = play.Card;
        var owner = acrobatics.Owner;
        CardPileCmd.Draw(cs, acrobatics.Vars.Cards.BaseValue, owner);
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, DiscardCount);
        var cardModel = CardSelectCmd.FromHandForDiscard(cs, owner, prefs, null, ChoiceSource.For(acrobatics, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Discard(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
