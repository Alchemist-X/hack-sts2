using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Wither : CardDef
{
    public const string CardId = "WITHER";
    public const int Cost = -1;
    public const decimal BaseDamage = 3m;
    public const decimal FakeUpgradeDamage = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Wither()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool HasTurnEndInHandEffect => true;

    public override CardExtraState? CreateExtraState() => new WitherState();

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Unpowered | ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        CreatureCmd.Damage(cs, card.Owner, card.Vars.Damage, card, null);
    }

    public int GetFakeUpgradeLevel(CardInstance card) => StateOf(card).FakeUpgradeLevel;

    public void FakeUpgrade(CardInstance card)
    {
        StateOf(card).FakeUpgradeLevel++;
        card.Vars.Damage.UpgradeValueBy(FakeUpgradeDamage);
    }

    private WitherState StateOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!ReferenceEquals(card.Def, this))
        {
            throw new ArgumentException($"{card} is not a {CardId}.", nameof(card));
        }
        return (WitherState)card.Extra!;
    }

    private sealed class WitherState : CardExtraState
    {
        public int FakeUpgradeLevel { get; set; }

        public override CardExtraState DeepClone() => (WitherState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(CardId);
            w.WriteInt32(FakeUpgradeLevel);
        }
    }
}
