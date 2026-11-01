namespace Sts2Sim.Core.Cards;

public sealed class DevourLife : CardDef
{
    public const string CardId = "DEVOUR_LIFE";
    public const int Cost = 1;
    public const string DevourLifePowerId = "DEVOUR_LIFE_POWER";
    public const string DevourLifePowerKey = "DevourLifePower";
    public const decimal BaseAmount = 1m;
    public const decimal AmountUpgrade = 1m;

    public DevourLife()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PowerModel>(DevourLifePowerKey, BaseAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var devourLife = play.Card;
        var owner = devourLife.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(DevourLifePowerId), owner, devourLife.Vars[DevourLifePowerKey].BaseValue, owner, devourLife);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[DevourLifePowerKey].UpgradeValueBy(AmountUpgrade);
    }
}
