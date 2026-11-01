namespace Sts2Sim.Core.Cards;

public sealed class Skim : CardDef
{
    public const string CardId = "SKIM";
    public const int Cost = 1;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;

    public Skim()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var skim = play.Card;
        CardPileCmd.Draw(cs, skim.Vars.Cards.BaseValue, skim.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
