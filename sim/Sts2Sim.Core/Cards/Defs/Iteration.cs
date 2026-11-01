namespace Sts2Sim.Core.Cards;

public sealed class Iteration : CardDef
{
    public const string CardId = "ITERATION";
    public const int Cost = 1;
    public const string IterationPowerKey = "IterationPower";
    public const decimal BaseIteration = 2m;
    public const decimal IterationUpgrade = 1m;
    public const string IterationPowerId = "ITERATION_POWER";

    public Iteration()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(IterationPowerKey, BaseIteration)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var iteration = play.Card;
        var owner = iteration.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(IterationPowerId), owner, iteration.Vars[IterationPowerKey].BaseValue, owner, iteration);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[IterationPowerKey].UpgradeValueBy(IterationUpgrade);
    }
}
