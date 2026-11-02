using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class AshenStrike : CardDef
{
    public const string CardId = "ASHEN_STRIKE";
    public const int Cost = 1;
    public const decimal CalculationBase = 6m;
    public const decimal ExtraDamage = 3m;
    public const decimal ExtraDamageUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> ExhaustPileMultiplier = static (_, card, _) => card.Owner.ExhaustPile.Cards.Count;

    public AshenStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(ExhaustPileMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var ashenStrike = play.Card;
        var calculatedDamage = ashenStrike.Vars.CalculatedDamage;
        DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, ashenStrike, t)).FromCard(ashenStrike, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
