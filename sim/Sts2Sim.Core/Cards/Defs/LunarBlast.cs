namespace Sts2Sim.Core.Cards;

public sealed class LunarBlast : CardDef
{
    public const string CardId = "LUNAR_BLAST";
    public const int Cost = 0;
    public const decimal BaseDamage = 4m;
    public const decimal DamageUpgrade = 1m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> SkillsPlayedThisTurn = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Skill && e.CardPlay.PlayerId == ownerId);
    };

    public LunarBlast()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(SkillsPlayedThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var lunarBlast = play.Card;
        int hitCount = (int)((CalculatedVar)lunarBlast.Vars[CalculatedHitsKey]).Calculate(cs, lunarBlast, target);
        DamageCmd.Attack(cs, lunarBlast.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(lunarBlast, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
