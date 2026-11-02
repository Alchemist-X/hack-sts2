namespace Sts2Sim.Core.Cards;

public sealed class Prophesize : CardDef
{
    public const string CardId = "PROPHESIZE";
    public const int Cost = 2;
    public const int BaseCards = 6;
    public const decimal CardsUpgrade = 3m;

    public Prophesize()
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
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
