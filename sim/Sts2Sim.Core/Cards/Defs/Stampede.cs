namespace Sts2Sim.Core.Cards;

public sealed class Stampede : CardDef
{
    public const string CardId = "STAMPEDE";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const string PowerKey = "Power";
    public const decimal BasePower = 1m;
    public const string StampedePowerId = "STAMPEDE_POWER";

    public Stampede()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var stampede = play.Card;
        var owner = stampede.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(StampedePowerId), owner, stampede.Vars[PowerKey].BaseValue, owner, stampede);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
