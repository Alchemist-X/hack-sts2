namespace Sts2Sim.Core.Cards;

public sealed class Corruption : CardDef
{
    public const string CardId = "CORRUPTION";
    public const int Cost = 3;
    public const int CostUpgrade = -1;
    public const decimal BasePower = 1m;
    public const string PowerVarName = "Power";
    public const string CorruptionPowerId = "CORRUPTION_POWER";

    public Corruption()
        : base(CardId, Cost, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var corruption = play.Card;
        var owner = corruption.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CorruptionPowerId), owner, corruption.Vars[PowerVarName].BaseValue, owner, corruption);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
