namespace Sts2Sim.Core.Cards;

public sealed class Supermassive : CardDef
{
    public const string CardId = "SUPERMASSIVE";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const decimal BaseExtraDamage = 3m;
    public const decimal ExtraDamageUpgrade = 1m;

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardsGeneratedThisCombat = static (cs, card, _) =>
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardGeneratedEntry>(c => c.CreatorId == ownerId);
    };

    public Supermassive()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CardsGeneratedThisCombat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var supermassive = play.Card;
        DamageCmd.Attack(cs, (c, t) => supermassive.Vars.CalculatedDamage.Calculate(c, supermassive, t)).FromCard(supermassive, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
