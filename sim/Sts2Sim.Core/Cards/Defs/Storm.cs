namespace Sts2Sim.Core.Cards;

public sealed class Storm : CardDef
{
    public const string CardId = "STORM";
    public const int Cost = 1;
    public const string StormPowerId = "STORM_POWER";
    public const string StormPowerVar = "StormPower";
    public const decimal BaseStorm = 1m;
    public const decimal StormUpgrade = 1m;

    public Storm()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(StormPowerVar, BaseStorm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var storm = play.Card;
        var owner = storm.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(StormPowerId), owner, storm.Vars[StormPowerVar].BaseValue, owner, storm);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StormPowerVar].UpgradeValueBy(StormUpgrade);
    }
}
