namespace Sts2Sim.Core.Cards;

public sealed class StoneArmor : CardDef
{
    public const string CardId = "STONE_ARMOR";
    public const int Cost = 1;
    public const string PowerVarName = "PlatingPower";
    public const decimal BasePlating = 4m;
    public const decimal PlatingUpgrade = 2m;
    public const string PlatingPowerId = "PLATING_POWER";

    public StoneArmor()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePlating)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PlatingPowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PlatingUpgrade);
    }
}
