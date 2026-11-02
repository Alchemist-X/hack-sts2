using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class TheScythe : CardDef
{
    public const string CardId = "THE_SCYTHE";
    public const int Cost = 2;
    public const string IncreaseKey = "Increase";
    public const int BaseDamage = 13;
    public const decimal BaseIncrease = 5m;
    public const decimal IncreaseUpgrade = 2m;
    public const ushort StateTag = 0xCB61;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public TheScythe()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new IntVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new TheScytheState();

    public static int GetCurrentDamage(CardInstance card) => ((TheScytheState)card.Extra!).CurrentDamage;

    public static int GetIncreasedDamage(CardInstance card) => ((TheScytheState)card.Extra!).IncreasedDamage;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        int intValue = card.Vars[IncreaseKey].IntValue;
        BuffFromPlay(card, intValue);
        var deckVersion = card.FindDeckVersion();
        if (deckVersion != null && deckVersion.Def is TheScythe)
        {
            BuffFromPlay(deckVersion, intValue);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[IncreaseKey].UpgradeValueBy(IncreaseUpgrade);
    }

    public override void AfterDowngraded(CardInstance card)
    {
        UpdateDamage(card);
    }

    private static void BuffFromPlay(CardInstance card, int extraDamage)
    {
        ((TheScytheState)card.Extra!).IncreasedDamage += extraDamage;
        UpdateDamage(card);
    }

    private static void UpdateDamage(CardInstance card)
    {
        var state = (TheScytheState)card.Extra!;
        state.CurrentDamage = BaseDamage + state.IncreasedDamage;
        card.Vars.Damage.BaseValue = state.CurrentDamage;
    }

    private sealed class TheScytheState : CardExtraState
    {
        public int CurrentDamage { get; set; } = BaseDamage;

        public int IncreasedDamage { get; set; }

        public override CardExtraState DeepClone() => (TheScytheState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(CurrentDamage);
            w.WriteInt32(IncreasedDamage);
        }
    }
}
