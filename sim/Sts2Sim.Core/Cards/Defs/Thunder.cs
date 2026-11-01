namespace Sts2Sim.Core.Cards;

public sealed class Thunder : CardDef
{
    public const string CardId = "THUNDER";
    public const int Cost = 1;
    public const string ThunderPowerId = "THUNDER_POWER";
    public const string ThunderPowerVar = "ThunderPower";
    public const decimal BaseThunder = 8m;
    public const decimal ThunderUpgrade = 3m;

    public Thunder()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ThunderPowerVar, BaseThunder)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var thunder = play.Card;
        var owner = thunder.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ThunderPowerId), owner, thunder.Vars[ThunderPowerVar].BaseValue, owner, thunder);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ThunderPowerVar].UpgradeValueBy(ThunderUpgrade);
    }
}
