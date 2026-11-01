namespace Sts2Sim.Core.Cards;

public sealed class Bully : CardDef
{
    public const string CardId = "BULLY";
    public const int Cost = 0;
    public const decimal CalculationBase = 4m;
    public const decimal ExtraDamage = 2m;
    public const decimal ExtraDamageUpgrade = 1m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> VulnerableMultiplier = static (_, _, target) => target?.GetPowerAmount<VulnerablePower>() ?? 0;

    public Bully()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(VulnerableMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var bully = play.Card;
        var calculatedDamage = bully.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, bully, t)).FromCard(bully, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
