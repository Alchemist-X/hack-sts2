namespace Sts2Sim.Core.Cards;

public sealed class Radiate : CardDef
{
    public const string CardId = "RADIATE";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const int BaseStars = 1;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const decimal DamageUpgrade = 1m;
    public const string CalculatedHitsKey = "CalculatedHits";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> StarsGainedThisTurn = static (cs, card, _) =>
    {
        var owner = card.Owner;
        return cs.History.SumThisTurn<StarsModifiedEntry>(cs, e => e.Amount, e => e.Amount > 0 && e.IsByActor(owner));
    };

    public Radiate()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new StarsVar(BaseStars),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(StarsGainedThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var radiate = play.Card;
        int hitCount = (int)((CalculatedVar)radiate.Vars[CalculatedHitsKey]).Calculate(cs, radiate, play.Target);
        DamageCmd.Attack(cs, radiate.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(radiate, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
