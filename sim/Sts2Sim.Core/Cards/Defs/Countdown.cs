namespace Sts2Sim.Core.Cards;

public sealed class Countdown : CardDef
{
    public const string CardId = "COUNTDOWN";
    public const int Cost = 1;
    public const string CountdownPowerId = "COUNTDOWN_POWER";
    public const string PowerVarName = "CountdownPower";
    public const decimal BaseCountdown = 6m;
    public const decimal CountdownUpgrade = 3m;

    public Countdown()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseCountdown)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var countdown = play.Card;
        var owner = countdown.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CountdownPowerId), owner, countdown.Vars[PowerVarName].BaseValue, owner, countdown);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(CountdownUpgrade);
    }
}
