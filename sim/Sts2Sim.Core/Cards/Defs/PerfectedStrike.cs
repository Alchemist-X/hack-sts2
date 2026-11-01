using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class PerfectedStrike : CardDef
{
    public const string CardId = "PERFECTED_STRIKE";
    public const int Cost = 2;
    public const decimal CalculationBase = 6m;
    public const decimal ExtraDamage = 2m;
    public const decimal ExtraDamageUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> StrikeCount = static (_, card, _) => CountStrikes(card.Owner);

    public PerfectedStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(StrikeCount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var perfectedStrike = play.Card;
        var calculatedDamage = perfectedStrike.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, perfectedStrike, t)).FromCard(perfectedStrike, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }

    private static int CountStrikes(PlayerState owner)
    {
        int count = 0;
        var piles = owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].HasTag(CardTag.Strike))
                {
                    count++;
                }
            }
        }
        return count;
    }
}
