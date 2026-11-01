namespace Sts2Sim.Core.Cards;

public sealed class Smokestack : CardDef
{
    public const string CardId = "SMOKESTACK";
    public const int Cost = 1;
    public const string PowerVarName = "SmokestackPower";
    public const decimal BasePower = 5m;
    public const decimal PowerUpgrade = 2m;
    public const string SmokestackPowerId = "SMOKESTACK_POWER";

    public Smokestack()
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
        PowerCmd.Apply(cs, SmokestackPowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
