namespace Sts2Sim.Core.Cards;

public sealed class Barrage : CardDef
{
    public const string CardId = "BARRAGE";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const decimal BaseCalculationBase = 0m;
    public const decimal BaseCalculationExtra = 1m;
    public const decimal DamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public Barrage()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(BaseCalculationBase),
        new CalculationExtraVar(BaseCalculationExtra),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(static (CombatState _, CardInstance card, CreatureState? _) => card.Owner.OrbQueue.Orbs.Count)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var barrage = play.Card;
        int hitCount = (int)((CalculatedVar)barrage.Vars[CalculatedHitsKey]).Calculate(cs, barrage, target);
        DamageCmd.Attack(cs, barrage.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(barrage, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
