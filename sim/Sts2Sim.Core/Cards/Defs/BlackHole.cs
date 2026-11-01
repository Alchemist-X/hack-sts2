namespace Sts2Sim.Core.Cards;

public sealed class BlackHole : CardDef
{
    public const string CardId = "BLACK_HOLE";
    public const int Cost = 1;
    public const string BlackHolePowerId = "BLACK_HOLE_POWER";
    public const string BlackHolePowerKey = "BlackHolePower";
    public const decimal BaseAmount = 3m;
    public const decimal AmountUpgrade = 1m;

    public BlackHole()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PowerModel>(BlackHolePowerKey, BaseAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var blackHole = play.Card;
        var owner = blackHole.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(BlackHolePowerId), owner, blackHole.Vars[BlackHolePowerKey].BaseValue, owner, blackHole);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[BlackHolePowerKey].UpgradeValueBy(AmountUpgrade);
    }
}
