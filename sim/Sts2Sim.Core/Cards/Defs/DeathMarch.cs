namespace Sts2Sim.Core.Cards;

public sealed class DeathMarch : CardDef
{
    public const string CardId = "DEATH_MARCH";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const decimal BaseExtraDamage = 4m;
    public const decimal DamageUpgrade = 1m;
    public const decimal ExtraDamageUpgrade = 2m;

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardsDrawnThisTurn = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountThisTurn<CardDrawnEntry>(cs, e => e.Stamp.ActorId == ownerId && !e.FromHandDraw);
    };

    public DeathMarch()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CardsDrawnThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var deathMarch = play.Card;
        DamageCmd.Attack(cs, (c, t) => deathMarch.Vars.CalculatedDamage.Calculate(c, deathMarch, t)).FromCard(deathMarch, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(DamageUpgrade);
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
