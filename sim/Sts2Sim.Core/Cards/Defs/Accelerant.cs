namespace Sts2Sim.Core.Cards;

public sealed class Accelerant : CardDef
{
    public const string CardId = "ACCELERANT";
    public const int Cost = 1;
    public const string AccelerantPowerId = "ACCELERANT_POWER";
    public const string PowerVarName = "Accelerant";
    public const decimal BaseAccelerant = 1m;
    public const decimal AccelerantUpgrade = 1m;

    public Accelerant()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseAccelerant)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var accelerant = play.Card;
        var owner = accelerant.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(AccelerantPowerId), owner, accelerant.Vars[PowerVarName].BaseValue, owner, accelerant);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(AccelerantUpgrade);
    }
}
