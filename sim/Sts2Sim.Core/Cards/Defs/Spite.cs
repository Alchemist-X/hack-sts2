namespace Sts2Sim.Core.Cards;

public sealed class Spite : CardDef
{
    public const string CardId = "SPITE";
    public const int Cost = 0;
    public const decimal BaseDamage = 5m;
    public const int BaseRepeat = 2;
    public const decimal RepeatUpgrade = 1m;

    public Spite()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var spite = play.Card;
        int hitCount = !LostHpThisTurn(cs, spite.Owner) ? 1 : spite.Vars.Repeat.IntValue;
        DamageCmd.Attack(cs, spite.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(spite, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }

    private static bool LostHpThisTurn(CombatState cs, CreatureState creature)
    {
        int creatureId = creature.CombatId;
        return cs.History.AnyThisTurn<DamageReceivedEntry>(cs, e => e.ReceiverId == creatureId && e.Result.UnblockedDamage > 0);
    }
}
