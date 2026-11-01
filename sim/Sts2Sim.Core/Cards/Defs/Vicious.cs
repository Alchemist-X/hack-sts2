namespace Sts2Sim.Core.Cards;

public sealed class Vicious : CardDef
{
    public const string CardId = "VICIOUS";
    public const int Cost = 1;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;
    public const string ViciousPowerId = "VICIOUS_POWER";

    public Vicious()
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
        PowerCmd.Apply(cs, ViciousPowerId, owner, card.Vars.Cards.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
