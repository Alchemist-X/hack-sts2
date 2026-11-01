namespace Sts2Sim.Core.Cards;

public sealed class Hailstorm : CardDef
{
    public const string CardId = "HAILSTORM";
    public const int Cost = 1;
    public const string HailstormPowerId = "HAILSTORM_POWER";
    public const string HailstormPowerVar = "HailstormPower";
    public const decimal BaseHailstorm = 6m;
    public const decimal HailstormUpgrade = 2m;

    public Hailstorm()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(HailstormPowerVar, BaseHailstorm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hailstorm = play.Card;
        var owner = hailstorm.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(HailstormPowerId), owner, hailstorm.Vars[HailstormPowerVar].BaseValue, owner, hailstorm);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[HailstormPowerVar].UpgradeValueBy(HailstormUpgrade);
    }
}
