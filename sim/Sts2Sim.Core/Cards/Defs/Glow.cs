namespace Sts2Sim.Core.Cards;

public sealed class Glow : CardDef
{
    public const string CardId = "GLOW";
    public const int Cost = 1;
    public const string DrawCardsNextTurnPowerId = "DRAW_CARDS_NEXT_TURN_POWER";
    public const int BaseStars = 1;
    public const int BaseCards = 1;
    public const decimal StarsUpgrade = 1m;

    public Glow()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(BaseStars),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var glow = play.Card;
        var owner = glow.Owner;
        PlayerCmd.GainStars(cs, glow.Vars.Stars.BaseValue, owner);
        CardPileCmd.Draw(cs, glow.Vars.Cards.BaseValue, owner);
        PowerCmd.Apply(cs, PowerRegistry.Create(DrawCardsNextTurnPowerId), owner, glow.Vars.Cards.BaseValue, owner, glow);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Stars.UpgradeValueBy(StarsUpgrade);
    }
}
