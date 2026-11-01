namespace Sts2Sim.Core.Cards;

public sealed class Rend : CardDef
{
    public const string CardId = "REND";
    public const int Cost = 1;
    public const decimal BaseCalculationBase = 10m;
    public const decimal BaseExtraDamage = 5m;
    public const decimal ExtraDamageUpgrade = 3m;
    public const decimal CalculationBaseUpgrade = 2m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> DebuffMultiplier =
        static (_, _, target) => target != null ? target.Powers.Count(ShouldCountPower) : 0;

    public Rend()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(DebuffMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var rend = play.Card;
        DamageCmd.Attack(cs, (c, t) => rend.Vars.CalculatedDamage.Calculate(c, rend, t)).FromCard(rend, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }

    private static bool ShouldCountPower(PowerModel power)
    {
        if (power.TypeForCurrentAmount == PowerType.Debuff)
        {
            return power is not ITemporaryPower;
        }
        return false;
    }
}
