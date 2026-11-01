namespace Sts2Sim.Core.Cards;

public sealed class Stack : CardDef
{
    public const string CardId = "STACK";
    public const int Cost = 1;
    public const decimal BaseCalculationBase = 0m;
    public const decimal BaseCalculationExtra = 1m;
    public const decimal CalculationBaseUpgrade = 3m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> DiscardMultiplier =
        static (_, card, _) => card.Owner.DiscardPile.Cards.Count;

    public Stack()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new CalculationExtraVar(BaseCalculationExtra),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(DiscardMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var stack = play.Card;
        var calculatedBlock = stack.Vars.CalculatedBlock;
        CreatureCmd.GainBlock(cs, stack.Owner, calculatedBlock.Calculate(cs, stack, play.Target), calculatedBlock.Props, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }
}
