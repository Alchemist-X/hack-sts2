namespace Sts2Sim.Core.Cards;

public sealed class MementoMori : CardDef
{
    public const string CardId = "MEMENTO_MORI";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal BaseExtraDamage = 4m;
    public const decimal DamageUpgrade = 2m;
    public const decimal ExtraDamageUpgrade = 1m;

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardsDiscardedThisTurn = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountThisTurn<CardDiscardedEntry>(cs, e => e.Stamp.ActorId == ownerId);
    };

    public MementoMori()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CardsDiscardedThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var mementoMori = play.Card;
        DamageCmd.Attack(cs, (c, t) => mementoMori.Vars.CalculatedDamage.Calculate(c, mementoMori, t)).FromCard(mementoMori, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(DamageUpgrade);
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
