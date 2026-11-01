namespace Sts2Sim.Core.Cards;

public sealed class Flechettes : CardDef
{
    public const string CardId = "FLECHETTES";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const decimal DamageUpgrade = 2m;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const string CalculatedHitsKey = "CalculatedHits";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> SkillsInHand = static (_, card, _) => CountSkills(card.Owner.Hand.Cards);

    public Flechettes()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(SkillsInHand)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var flechettes = play.Card;
        int hits = (int)((CalculatedVar)flechettes.Vars[CalculatedHitsKey]).Calculate(cs, flechettes, target);
        DamageCmd.Attack(cs, flechettes.Vars.Damage.BaseValue).WithHitCount(hits).FromCard(flechettes, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private static int CountSkills(IReadOnlyList<CardInstance> cards)
    {
        int count = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Type == CardType.Skill)
            {
                count++;
            }
        }
        return count;
    }
}
