namespace Sts2Sim.Core.Cards;

public sealed class Whirlwind : CardDef
{
    public const string CardId = "WHIRLWIND";
    public const int Cost = 0;
    public const decimal BaseDamage = 5m;
    public const decimal DamageUpgrade = 3m;

    public Whirlwind()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies, costsX: true)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var whirlwind = play.Card;
        int hitCount = whirlwind.ResolveEnergyXValue(cs);
        DamageCmd.Attack(cs, whirlwind.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(whirlwind, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
