namespace Sts2Sim.Core.Cards;

public sealed class KnifeTrap : CardDef
{
    public const string CardId = "KNIFE_TRAP";
    public const int Cost = 2;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const string CalculatedShivsKey = "CalculatedShivs";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> ExhaustedShivs = static (_, card, _) => CountShivs(card.Owner.ExhaustPile.Cards);

    public KnifeTrap()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedShivsKey).WithMultiplier(ExhaustedShivs)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var knifeTrap = play.Card;
        var exhaustCards = knifeTrap.Owner.ExhaustPile.Cards;
        var shivs = new List<CardInstance>(exhaustCards.Count);
        for (int i = 0; i < exhaustCards.Count; i++)
        {
            if (exhaustCards[i].HasTag(CardTag.Shiv))
            {
                shivs.Add(exhaustCards[i]);
            }
        }
        for (int i = 0; i < shivs.Count; i++)
        {
            var current = shivs[i];
            if (knifeTrap.IsUpgraded)
            {
                CardCmd.Upgrade(cs, current);
            }
            CardCmd.AutoPlay(cs, current, target);
        }
    }

    private static int CountShivs(IReadOnlyList<CardInstance> cards)
    {
        int count = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].HasTag(CardTag.Shiv))
            {
                count++;
            }
        }
        return count;
    }
}
