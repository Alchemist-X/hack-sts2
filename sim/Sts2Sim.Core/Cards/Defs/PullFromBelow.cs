namespace Sts2Sim.Core.Cards;

public sealed class PullFromBelow : CardDef
{
    public const string CardId = "PULL_FROM_BELOW";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const decimal DamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> EtherealPlaysThisCombat = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardPlayFinishedEntry>(e => e.CardPlay.PlayerId == ownerId && e.WasEthereal);
    };

    public PullFromBelow()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(EtherealPlaysThisCombat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var pullFromBelow = play.Card;
        int hitCount = (int)((CalculatedVar)pullFromBelow.Vars[CalculatedHitsKey]).Calculate(cs, pullFromBelow, target);
        DamageCmd.Attack(cs, pullFromBelow.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(pullFromBelow, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
