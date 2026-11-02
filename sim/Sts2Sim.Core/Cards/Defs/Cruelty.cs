namespace Sts2Sim.Core.Cards;

public sealed class Cruelty : CardDef
{
    public const string CardId = "CRUELTY";
    public const int Cost = 1;
    public const string CrueltyPowerId = "CRUELTY_POWER";
    public const string PowerVarName = "CrueltyPower";
    public const decimal BaseCruelty = 25m;
    public const decimal CrueltyUpgrade = 25m;

    public Cruelty()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseCruelty)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var cruelty = play.Card;
        var owner = cruelty.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CrueltyPowerId), owner, cruelty.Vars[PowerVarName].BaseValue, owner, cruelty);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(CrueltyUpgrade);
    }
}
