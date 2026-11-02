namespace Sts2Sim.Core.Cards;

public sealed class ExpectAFight : CardDef
{
    public const string CardId = "EXPECT_A_FIGHT";
    public const int Cost = 3;
    public const decimal BaseCalculationBase = 15m;
    public const decimal BaseCalculationExtra = 5m;
    public const decimal CalculationBaseUpgrade = 1m;
    public const decimal CalculationExtraUpgrade = 3m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> StrengthMultiplier = static (_, card, _) => Math.Max(0, card.Owner.GetPowerAmount<StrengthPower>());

    public ExpectAFight()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new CalculationExtraVar(BaseCalculationExtra),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(StrengthMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var expectAFight = play.Card;
        var calculatedBlock = expectAFight.Vars.CalculatedBlock;
        CreatureCmd.GainBlock(cs, expectAFight.Owner, calculatedBlock.Calculate(cs, expectAFight, play.Target), calculatedBlock.Props, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
        card.Vars.CalculationExtra.UpgradeValueBy(CalculationExtraUpgrade);
    }
}
