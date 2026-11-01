namespace Sts2Sim.Core.Cards;

public sealed class Envenom : CardDef
{
    public const string CardId = "ENVENOM";
    public const int Cost = 2;
    public const string EnvenomPowerId = "ENVENOM_POWER";
    public const string PowerVarName = "EnvenomPower";
    public const decimal BaseEnvenom = 1m;
    public const decimal EnvenomUpgrade = 1m;

    public Envenom()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseEnvenom)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var envenom = play.Card;
        var owner = envenom.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(EnvenomPowerId), owner, envenom.Vars[PowerVarName].BaseValue, owner, envenom);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(EnvenomUpgrade);
    }
}
