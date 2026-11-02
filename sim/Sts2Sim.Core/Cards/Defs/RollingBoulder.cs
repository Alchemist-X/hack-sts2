namespace Sts2Sim.Core.Cards;

public sealed class RollingBoulder : CardDef
{
    public const string CardId = "ROLLING_BOULDER";
    public const int Cost = 3;
    public const string PowerVarName = "RollingBoulderPower";
    public const string IncrementAmountKey = "IncrementAmount";
    public const decimal BasePower = 5m;
    public const int IncrementAmount = 5;
    public const decimal PowerUpgrade = 5m;
    public const string RollingBoulderPowerId = "ROLLING_BOULDER_POWER";

    public RollingBoulder()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePower),
        new DynamicVar(IncrementAmountKey, IncrementAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, RollingBoulderPowerId, owner, card.Vars[PowerVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
