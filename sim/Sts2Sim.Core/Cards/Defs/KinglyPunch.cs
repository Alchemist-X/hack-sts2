namespace Sts2Sim.Core.Cards;

public sealed class KinglyPunch : CardDef
{
    public const string CardId = "KINGLY_PUNCH";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 2m;
    public const string IncreaseKey = "Increase";
    public const decimal BaseIncrease = 4m;
    public const decimal IncreaseUpgrade = 2m;
    public const ushort ExtraStateTag = 0xCA32;

    public KinglyPunch()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new PunchState();

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var kinglyPunch = play.Card;
        DamageCmd.Attack(cs, kinglyPunch.Vars.Damage.BaseValue).FromCard(kinglyPunch, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[IncreaseKey].UpgradeValueBy(IncreaseUpgrade);
    }

    public override void AfterDowngraded(CardInstance card)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += StateOf(card).ExtraDamage;
    }

    public static decimal GetExtraDamage(CardInstance card) => StateOf(card).ExtraDamage;

    private static PunchState StateOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Extra as PunchState ?? throw new ArgumentException($"{card} is not a {CardId}.", nameof(card));
    }

    private sealed class Listener : CardListener
    {
        public Listener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);

        public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
        {
            var kinglyPunch = Card;
            if (!ReferenceEquals(card, kinglyPunch))
            {
                return;
            }
            decimal baseValue = kinglyPunch.Vars[IncreaseKey].BaseValue;
            var damage = kinglyPunch.Vars.Damage;
            damage.BaseValue += baseValue;
            StateOf(kinglyPunch).ExtraDamage += baseValue;
        }
    }

    private sealed class PunchState : CardExtraState
    {
        public decimal ExtraDamage { get; set; }

        public override CardExtraState DeepClone() => (PunchState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(ExtraStateTag);
            w.WriteDecimal(ExtraDamage);
        }
    }
}
