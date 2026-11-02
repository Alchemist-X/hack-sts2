namespace Sts2Sim.Core.Cards;

public sealed class Maul : CardDef
{
    public const string CardId = "MAUL";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const decimal DamageUpgrade = 1m;
    public const int HitCount = 2;
    public const string IncreaseKey = "Increase";
    public const decimal BaseIncrease = 2m;
    public const decimal IncreaseUpgrade = 1m;
    public const ushort ExtraStateTag = 0xCA33;

    public Maul()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new MaulState();

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var maul = play.Card;
        DamageCmd.Attack(cs, maul.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(maul, play).Targeting(target).Execute();
        decimal baseValue = maul.Vars[IncreaseKey].BaseValue;
        var piles = maul.Owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var current = cards[i];
                if (current.Def is Maul)
                {
                    BuffFromMaulPlay(current, baseValue);
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
        damage.BaseValue += StateOf(card).ExtraDamageFromMaulPlays;
    }

    public static decimal GetExtraDamageFromMaulPlays(CardInstance card) => StateOf(card).ExtraDamageFromMaulPlays;

    private static void BuffFromMaulPlay(CardInstance card, decimal extraDamage)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += extraDamage;
        StateOf(card).ExtraDamageFromMaulPlays += extraDamage;
    }

    private static MaulState StateOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Extra as MaulState ?? throw new ArgumentException($"{card} is not a {CardId}.", nameof(card));
    }

    private sealed class MaulState : CardExtraState
    {
        public decimal ExtraDamageFromMaulPlays { get; set; }

        public override CardExtraState DeepClone() => (MaulState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(ExtraStateTag);
            w.WriteDecimal(ExtraDamageFromMaulPlays);
        }
    }
}
