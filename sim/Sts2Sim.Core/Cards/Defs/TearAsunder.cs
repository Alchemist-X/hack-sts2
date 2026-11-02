namespace Sts2Sim.Core.Cards;

public sealed class TearAsunder : CardDef
{
    public const string CardId = "TEAR_ASUNDER";
    public const int Cost = 2;
    public const decimal BaseDamage = 5m;
    public const int BaseRepeat = 1;
    public const decimal DamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OnePlusUnblockedHitsTaken = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return 1 + cs.History.CountOf<DamageReceivedEntry>(e => e.ReceiverId == ownerId && e.Result.UnblockedDamage > 0);
    };

    public TearAsunder()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(OnePlusUnblockedHitsTaken)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var tearAsunder = play.Card;
        int hitCount = (int)((CalculatedVar)tearAsunder.Vars[CalculatedHitsKey]).Calculate(cs, tearAsunder, target);
        DamageCmd.Attack(cs, tearAsunder.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(tearAsunder, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
