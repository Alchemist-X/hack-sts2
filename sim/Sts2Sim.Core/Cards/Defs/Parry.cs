namespace Sts2Sim.Core.Cards;

public sealed class Parry : CardDef
{
    public const string CardId = "PARRY";
    public const int Cost = 1;
    public const string ParryPowerId = "PARRY_POWER";
    public const string ParryPowerVar = "ParryPower";
    public const decimal BaseParry = 10m;
    public const decimal ParryUpgrade = 4m;

    public Parry()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ParryPowerVar, BaseParry)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var parry = play.Card;
        var owner = parry.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ParryPowerId), owner, parry.Vars[ParryPowerVar].BaseValue, owner, parry);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ParryPowerVar].UpgradeValueBy(ParryUpgrade);
    }
}
