namespace Sts2Sim.Core.Cards;

public sealed class CrescentSpear : CardDef
{
    public const string CardId = "CRESCENT_SPEAR";
    public const int Cost = 1;
    public const int StarCost = 1;
    public const decimal CalculationBase = 8m;
    public const decimal ExtraDamage = 2m;
    public const decimal ExtraDamageUpgrade = 1m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> StarCostCardCount = static (_, card, _) => CountStarCostCards(card);

    public CrescentSpear()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(StarCostCardCount)
    };

    public static decimal CountStarCostCards(CardInstance card)
    {
        var piles = card.Owner.AllPiles;
        int count = 0;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var def = cards[i].Def;
                if (def.CanonicalStarCost >= 0 || def.HasStarCostX)
                {
                    count++;
                }
            }
        }
        return count;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var crescentSpear = play.Card;
        var calculatedDamage = crescentSpear.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, crescentSpear, t)).FromCard(crescentSpear, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
