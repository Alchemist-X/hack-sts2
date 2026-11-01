namespace Sts2Sim.Core.Cards;

public sealed class Claw : CardDef
{
    public const string CardId = "CLAW";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal BaseIncrease = 2m;
    public const decimal DamageUpgrade = 1m;
    public const decimal IncreaseUpgrade = 1m;
    public const string IncreaseKey = "Increase";
    public const ushort ClawStateTag = 0xCA31;

    public Claw()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new ClawState();

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var claw = play.Card;
        DamageCmd.Attack(cs, claw.Vars.Damage.BaseValue).FromCard(claw, play).Targeting(target).Execute();
        decimal baseValue = claw.Vars[IncreaseKey].BaseValue;
        var piles = claw.Owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var current = cards[i];
                if (current.Def is Claw def)
                {
                    def.BuffFromClawPlay(current, baseValue);
                }
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[IncreaseKey].UpgradeValueBy(IncreaseUpgrade);
    }

    public override void AfterDowngraded(CardInstance card)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += StateOf(card).ExtraDamageFromClawPlays;
    }

    public decimal GetExtraDamageFromClawPlays(CardInstance card) => StateOf(card).ExtraDamageFromClawPlays;

    private void BuffFromClawPlay(CardInstance card, decimal extraDamage)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += extraDamage;
        StateOf(card).ExtraDamageFromClawPlays += extraDamage;
    }

    private ClawState StateOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!ReferenceEquals(card.Def, this))
        {
            throw new ArgumentException($"{card} is not a {CardId}.", nameof(card));
        }
        return (ClawState)card.Extra!;
    }

    private sealed class ClawState : CardExtraState
    {
        public decimal ExtraDamageFromClawPlays { get; set; }

        public override CardExtraState DeepClone() => (ClawState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(ClawStateTag);
            w.WriteDecimal(ExtraDamageFromClawPlays);
        }
    }
}
