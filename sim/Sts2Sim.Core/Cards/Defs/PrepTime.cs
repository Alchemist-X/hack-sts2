namespace Sts2Sim.Core.Cards;

public sealed class PrepTime : CardDef
{
    public const string CardId = "PREP_TIME";
    public const int Cost = 1;
    public const string PowerVarName = "PrepTimePower";
    public const decimal BasePower = 4m;
    public const decimal PowerUpgrade = 2m;
    public const string PrepTimePowerId = "PREP_TIME_POWER";

    public PrepTime()
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
        PowerCmd.Apply(cs, PrepTimePowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
