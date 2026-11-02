namespace Sts2Sim.Core.Cards;

public sealed class Dismantle : CardDef
{
    public const string CardId = "DISMANTLE";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 2m;
    public const int HitsWithVulnerable = 2;
    public const int HitsWithoutVulnerable = 1;

    public Dismantle()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var dismantle = play.Card;
        int hitCount = !target.HasPower<VulnerablePower>() ? HitsWithoutVulnerable : HitsWithVulnerable;
        DamageCmd.Attack(cs, dismantle.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(dismantle, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
