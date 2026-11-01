namespace Sts2Sim.Core.Cards;

public sealed class Skewer : CardDef
{
    public const string CardId = "SKEWER";
    public const int Cost = 0;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 3m;

    public Skewer()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy, costsX: true)
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
        var skewer = play.Card;
        DamageCmd.Attack(cs, skewer.Vars.Damage.BaseValue).WithHitCount(skewer.ResolveEnergyXValue(cs)).FromCard(skewer, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
