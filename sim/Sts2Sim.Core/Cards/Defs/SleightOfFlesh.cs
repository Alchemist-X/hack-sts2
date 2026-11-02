namespace Sts2Sim.Core.Cards;

public sealed class SleightOfFlesh : CardDef
{
    public const string CardId = "SLEIGHT_OF_FLESH";
    public const int Cost = 2;
    public const string PowerVarName = "SleightOfFleshPower";
    public const decimal BasePower = 9m;
    public const decimal PowerUpgrade = 4m;
    public const string SleightOfFleshPowerId = "SLEIGHT_OF_FLESH_POWER";

    public SleightOfFlesh()
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
        PowerCmd.Apply(cs, SleightOfFleshPowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
