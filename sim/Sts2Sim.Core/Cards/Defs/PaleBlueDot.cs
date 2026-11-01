namespace Sts2Sim.Core.Cards;

public sealed class PaleBlueDot : CardDef
{
    public const string CardId = "PALE_BLUE_DOT";
    public const int Cost = 1;
    public const int BaseCards = 1;
    public const string CardPlayKey = "CardPlay";
    public const decimal BaseCardPlay = 5m;
    public const decimal CardsUpgrade = 1m;
    public const string PaleBlueDotPowerId = "PALE_BLUE_DOT_POWER";

    public PaleBlueDot()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new DynamicVar(CardPlayKey, BaseCardPlay)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var paleBlueDot = play.Card;
        var owner = paleBlueDot.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PaleBlueDotPowerId), owner, paleBlueDot.Vars.Cards.BaseValue, owner, paleBlueDot);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
