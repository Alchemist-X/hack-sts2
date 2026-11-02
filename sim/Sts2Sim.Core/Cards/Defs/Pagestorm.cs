namespace Sts2Sim.Core.Cards;

public sealed class Pagestorm : CardDef
{
    public const string CardId = "PAGESTORM";
    public const int Cost = 1;
    public const string PagestormPowerId = "PAGESTORM_POWER";
    public const int BaseCards = 1;
    public const int CostUpgrade = -1;

    public Pagestorm()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
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
        PowerCmd.Apply(cs, PowerRegistry.Create(PagestormPowerId), owner, card.Vars.Cards.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
