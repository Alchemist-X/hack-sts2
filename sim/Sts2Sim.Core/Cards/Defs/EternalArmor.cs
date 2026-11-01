namespace Sts2Sim.Core.Cards;

public sealed class EternalArmor : CardDef
{
    public const string CardId = "ETERNAL_ARMOR";
    public const int Cost = 3;
    public const string PlatingPowerId = "PLATING_POWER";
    public const string PlatingVarName = "PlatingPower";
    public const decimal BasePlating = 9m;
    public const decimal PlatingUpgrade = 3m;

    public EternalArmor()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PlatingVarName, BasePlating)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PlatingPowerId), owner, card.Vars[PlatingVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PlatingVarName].UpgradeValueBy(PlatingUpgrade);
    }
}
