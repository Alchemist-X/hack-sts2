namespace Sts2Sim.Core.Cards;

public sealed class Calcify : CardDef
{
    public const string CardId = "CALCIFY";
    public const int Cost = 1;
    public const string CalcifyPowerId = "CALCIFY_POWER";
    public const string CalcifyPowerKey = "CalcifyPower";
    public const decimal BaseAmount = 4m;
    public const decimal AmountUpgrade = 2m;

    public Calcify()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PowerModel>(CalcifyPowerKey, BaseAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var calcify = play.Card;
        var owner = calcify.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CalcifyPowerId), owner, calcify.Vars[CalcifyPowerKey].BaseValue, owner, calcify);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[CalcifyPowerKey].UpgradeValueBy(AmountUpgrade);
    }
}
