namespace Sts2Sim.Core.Cards;

public sealed class HeavenlyDrill : CardDef
{
    public const string CardId = "HEAVENLY_DRILL";
    public const int Cost = 0;
    public const decimal BaseDamage = 8m;
    public const int BaseEnergy = 4;
    public const int HitMultiplier = 2;
    public const decimal DamageUpgrade = 2m;

    public HeavenlyDrill()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy, costsX: true)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var heavenlyDrill = play.Card;
        int hitCount = heavenlyDrill.ResolveEnergyXValue(cs);
        if (hitCount >= heavenlyDrill.Vars.Energy.IntValue)
        {
            hitCount *= HitMultiplier;
        }
        DamageCmd.Attack(cs, heavenlyDrill.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(heavenlyDrill, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
