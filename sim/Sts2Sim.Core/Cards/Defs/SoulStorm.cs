namespace Sts2Sim.Core.Cards;

public sealed class SoulStorm : CardDef
{
    public const string CardId = "SOUL_STORM";
    public const int Cost = 1;
    public const decimal BaseCalculationBase = 9m;
    public const decimal BaseExtraDamage = 4m;
    public const decimal ExtraDamageUpgrade = 2m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> ExhaustedSoulMultiplier =
        static (_, card, _) => card.Owner.ExhaustPile.Cards.Count(c => c.Id == Soul.CardId);

    public SoulStorm()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(ExhaustedSoulMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var soulStorm = play.Card;
        DamageCmd.Attack(cs, (c, t) => soulStorm.Vars.CalculatedDamage.Calculate(c, soulStorm, t)).FromCard(soulStorm, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
