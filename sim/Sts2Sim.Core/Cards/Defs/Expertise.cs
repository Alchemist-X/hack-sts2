namespace Sts2Sim.Core.Cards;

public sealed class Expertise : CardDef
{
    public const string CardId = "EXPERTISE";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    public Expertise()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var expertise = play.Card;
        var drawn = CardPileCmd.DrawCards(cs, expertise.Vars.Cards.IntValue, expertise.Owner);
        for (int i = 0; i < drawn.Count; i++)
        {
            CardCmd.ApplySingleTurnRetain(drawn[i]);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
