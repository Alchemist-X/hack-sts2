using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MindBlast : CardDef
{
    public const string CardId = "MIND_BLAST";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal CalculationBase = 0m;
    public const decimal ExtraDamage = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Innate });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> DrawPileSize = static (_, card, _) => card.Owner.DrawPile.Cards.Count;

    public MindBlast()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(DrawPileSize)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var mindBlast = play.Card;
        var calculatedDamage = mindBlast.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, mindBlast, t)).FromCard(mindBlast, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
