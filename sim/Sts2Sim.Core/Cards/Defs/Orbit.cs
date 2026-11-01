namespace Sts2Sim.Core.Cards;

public sealed class Orbit : CardDef
{
    public const string CardId = "ORBIT";
    public const int Cost = 2;
    public const string OrbitPowerId = "ORBIT_POWER";
    public const int BaseEnergy = 1;
    public const int CostUpgrade = -1;

    public Orbit()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(OrbitPowerId), owner, card.Vars.Energy.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
