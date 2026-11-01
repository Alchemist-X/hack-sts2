namespace Sts2Sim.Core.Cards;

public sealed class Arsenal : CardDef
{
    public const string CardId = "ARSENAL";
    public const int Cost = 1;
    public const string ArsenalPowerId = "ARSENAL_POWER";
    public const string PowerVarName = "ArsenalPower";
    public const decimal BaseArsenal = 1m;

    public Arsenal()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseArsenal)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var arsenal = play.Card;
        var owner = arsenal.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ArsenalPowerId), owner, arsenal.Vars[PowerVarName].BaseValue, owner, arsenal);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
