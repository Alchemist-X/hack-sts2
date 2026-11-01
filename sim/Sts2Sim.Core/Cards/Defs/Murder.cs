namespace Sts2Sim.Core.Cards;

public sealed class Murder : CardDef
{
    public const string CardId = "MURDER";
    public const int Cost = 3;
    public const decimal BaseDamage = 1m;
    public const decimal BaseExtraDamage = 1m;
    public const int CostUpgrade = -1;

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardsDrawnThisCombat = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardDrawnEntry>(e => e.Stamp.ActorId == ownerId);
    };

    public Murder()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CardsDrawnThisCombat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var murder = play.Card;
        DamageCmd.Attack(cs, (c, t) => murder.Vars.CalculatedDamage.Calculate(c, murder, t)).FromCard(murder, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
