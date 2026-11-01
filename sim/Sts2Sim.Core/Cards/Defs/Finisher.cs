namespace Sts2Sim.Core.Cards;

public sealed class Finisher : CardDef
{
    public const string CardId = "FINISHER";
    public const int Cost = 1;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> AttacksPlayedThisTurn = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Attack && e.CardPlay.PlayerId == ownerId);
    };

    public Finisher()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(AttacksPlayedThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var finisher = play.Card;
        int hitCount = (int)((CalculatedVar)finisher.Vars[CalculatedHitsKey]).Calculate(cs, finisher, target);
        DamageCmd.Attack(cs, finisher.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(finisher, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
