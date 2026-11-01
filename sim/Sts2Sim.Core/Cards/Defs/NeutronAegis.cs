namespace Sts2Sim.Core.Cards;

public sealed class NeutronAegis : CardDef
{
    public const string CardId = "NEUTRON_AEGIS";
    public const int Cost = 1;
    public const int StarCost = 5;
    public const string PlatingPowerId = "PLATING_POWER";
    public const string PlatingPowerVar = "PlatingPower";
    public const decimal BasePlating = 8m;
    public const decimal PlatingUpgrade = 3m;

    public NeutronAegis()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PlatingPowerVar, BasePlating)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var neutronAegis = play.Card;
        var owner = neutronAegis.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PlatingPowerId), owner, neutronAegis.Vars[PlatingPowerVar].BaseValue, owner, neutronAegis);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PlatingPowerVar].UpgradeValueBy(PlatingUpgrade);
    }
}
