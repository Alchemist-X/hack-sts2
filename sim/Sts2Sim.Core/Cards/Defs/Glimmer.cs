namespace Sts2Sim.Core.Cards;

public sealed class Glimmer : CardDef
{
    public const string CardId = "GLIMMER";
    public const int Cost = 1;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;
    public const string PutBackKey = "PutBack";
    public const decimal BasePutBack = 1m;

    public Glimmer()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new DynamicVar(PutBackKey, BasePutBack)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, card.Vars[PutBackKey].IntValue);
        var array = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToArray();
        if (array.Length != 0)
        {
            CardPileCmd.Add(cs, array, PileType.Draw, CardPilePosition.Top);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
