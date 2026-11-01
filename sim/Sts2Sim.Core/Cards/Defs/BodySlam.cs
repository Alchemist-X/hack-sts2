namespace Sts2Sim.Core.Cards;

public sealed class BodySlam : CardDef
{
    public const string CardId = "BODY_SLAM";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal CalculationBase = 0m;
    public const decimal ExtraDamage = 1m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> BlockMultiplier = static (_, card, _) => card.Owner.Block;

    public BodySlam()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(BlockMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var bodySlam = play.Card;
        var calculatedDamage = bodySlam.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, bodySlam, t)).FromCard(bodySlam, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
