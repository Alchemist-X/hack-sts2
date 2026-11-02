namespace Sts2Sim.Core.Cards;

public sealed class Speedster : CardDef
{
    public const string CardId = "SPEEDSTER";
    public const int Cost = 2;
    public const string PowerVarName = "SpeedsterPower";
    public const decimal BasePower = 2m;
    public const string SpeedsterPowerId = "SPEEDSTER_POWER";

    public Speedster()
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
        PowerCmd.Apply(cs, SpeedsterPowerId, owner, card.Vars[PowerVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
