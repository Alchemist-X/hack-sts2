namespace Sts2Sim.Core.Cards;

public sealed class FeelNoPain : CardDef
{
    public const string CardId = "FEEL_NO_PAIN";
    public const int Cost = 1;
    public const string FeelNoPainPowerId = "FEEL_NO_PAIN_POWER";
    public const string PowerVarName = "Power";
    public const decimal BasePower = 3m;
    public const decimal PowerUpgrade = 1m;

    public FeelNoPain()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FeelNoPainPowerId), owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
