namespace Sts2Sim.Core.Cards;

public sealed class Impatience : CardDef
{
    public const string CardId = "IMPATIENCE";
    public const int Cost = 0;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    public Impatience()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var impatience = play.Card;
        var owner = impatience.Owner;
        if (!owner.Hand.Cards.Any(c => c.Type == CardType.Attack))
        {
            CardPileCmd.Draw(cs, impatience.Vars.Cards.BaseValue, owner);
        }
    }
}
