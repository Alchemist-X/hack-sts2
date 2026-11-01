namespace Sts2Sim.Core.Cards;

public sealed class Rampage : CardDef
{
    public const string CardId = "RAMPAGE";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const string IncreaseKey = "Increase";
    public const decimal BaseIncrease = 5m;
    public const decimal IncreaseUpgrade = 5m;
    public const ushort StateTag = 0xCA31;

    public Rampage()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new RampageState();

    public static decimal GetExtraDamageFromPlays(CardInstance card) => ((RampageState)card.Extra!).ExtraDamageFromPlays;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var rampage = play.Card;
        DamageCmd.Attack(cs, rampage.Vars.Damage.BaseValue).FromCard(rampage, play).Targeting(target).Execute();
        var damage = rampage.Vars.Damage;
        damage.BaseValue += rampage.Vars[IncreaseKey].BaseValue;
        ((RampageState)rampage.Extra!).ExtraDamageFromPlays += rampage.Vars[IncreaseKey].BaseValue;
    }

    public override void AfterDowngraded(CardInstance card)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += ((RampageState)card.Extra!).ExtraDamageFromPlays;
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[IncreaseKey].UpgradeValueBy(IncreaseUpgrade);
    }

    private sealed class RampageState : CardExtraState
    {
        public decimal ExtraDamageFromPlays { get; set; }

        public override CardExtraState DeepClone() => (RampageState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteDecimal(ExtraDamageFromPlays);
        }
    }
}
