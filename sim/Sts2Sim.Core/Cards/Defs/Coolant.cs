namespace Sts2Sim.Core.Cards;

public sealed class Coolant : CardDef
{
    public const string CardId = "COOLANT";
    public const int Cost = 1;
    public const string CoolantPowerId = "COOLANT_POWER";
    public const string CoolantPowerVar = "CoolantPower";
    public const decimal BaseCoolant = 2m;
    public const decimal CoolantUpgrade = 1m;

    public Coolant()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CoolantPowerVar, BaseCoolant)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var coolant = play.Card;
        var owner = coolant.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CoolantPowerId), owner, coolant.Vars[CoolantPowerVar].BaseValue, owner, coolant);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[CoolantPowerVar].UpgradeValueBy(CoolantUpgrade);
    }
}
