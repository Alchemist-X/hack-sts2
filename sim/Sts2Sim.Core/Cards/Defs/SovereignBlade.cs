using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SovereignBlade : CardDef, IForgeBladeDef
{
    public const string CardId = "SOVEREIGN_BLADE";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal BaseDamage = 10m;
    public const decimal BaseRepeats = 1m;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const string SeekingEdgePowerId = "SEEKING_EDGE_POWER";
    public const string ParryPowerId = "PARRY_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> ParryMultiplier = static (_, card, _) => GetOwnerParryAmount(card);

    public SovereignBlade()
        : base(CardId, Cost, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(ParryMultiplier),
        new RepeatVar((int)BaseRepeats)
    };

    public override TargetType GetTargetType(CardInstance card) => HasSeekingEdge(card) ? TargetType.AllEnemies : TargetType.AnyEnemy;

    public override bool GetGainsBlock(CardInstance card) => GetOwnerParryAmount(card) > 0m;

    public override CardExtraState? CreateExtraState() => new BladeState();

    public static bool HasSeekingEdge(CardInstance card) => card.HasOwner && card.Owner.HasPower(SeekingEdgePowerId);

    public static decimal GetOwnerParryAmount(CardInstance card)
    {
        if (!card.HasOwner)
        {
            return 0m;
        }
        var owner = card.Owner;
        if (owner.FindPileContaining(card) == null)
        {
            return 0m;
        }
        return owner.GetPower(ParryPowerId)?.Amount ?? 0;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sovereignBlade = play.Card;
        var attackCommand = DamageCmd.Attack(cs, sovereignBlade.Vars.Damage.BaseValue).FromCard(sovereignBlade, play).WithHitCount(sovereignBlade.Vars.Repeat.IntValue);
        if (HasSeekingEdge(sovereignBlade))
        {
            attackCommand = attackCommand.TargetingAllOpponents();
        }
        else
        {
            var target = play.Target;
            ArgumentNullException.ThrowIfNull(target);
            attackCommand = attackCommand.Targeting(target);
        }
        attackCommand.Execute();
        if (GetOwnerParryAmount(sovereignBlade) > 0m)
        {
            var calculatedBlock = sovereignBlade.Vars.CalculatedBlock;
            CreatureCmd.GainBlock(cs, sovereignBlade.Owner, calculatedBlock.Calculate(cs, sovereignBlade, play.Target), calculatedBlock.Props, play);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }

    public override void AfterCloned(CardInstance card)
    {
        StateOf(card).CreatedThroughForge = false;
    }

    public override void AfterDowngraded(CardInstance card)
    {
        var state = StateOf(card);
        card.Vars.Damage.BaseValue = state.CurrentDamage;
        card.Vars.Repeat.BaseValue = state.CurrentRepeats;
    }

    public bool GetCreatedThroughForge(CardInstance card) => StateOf(card).CreatedThroughForge;

    public void SetCreatedThroughForge(CardInstance card, bool value) => StateOf(card).CreatedThroughForge = value;

    public decimal GetCurrentDamage(CardInstance card) => StateOf(card).CurrentDamage;

    public decimal GetCurrentRepeats(CardInstance card) => StateOf(card).CurrentRepeats;

    public void AddDamage(CardInstance card, decimal amount)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += amount;
        StateOf(card).CurrentDamage = card.Vars.Damage.BaseValue;
    }

    public void SetRepeats(CardInstance card, decimal amount)
    {
        card.Vars.Repeat.BaseValue = amount;
        StateOf(card).CurrentRepeats = card.Vars.Repeat.BaseValue;
    }

    private BladeState StateOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!ReferenceEquals(card.Def, this))
        {
            throw new ArgumentException($"{card} is not a {CardId}.", nameof(card));
        }
        return (BladeState)card.Extra!;
    }

    private sealed class BladeState : CardExtraState
    {
        public decimal CurrentDamage { get; set; } = BaseDamage;

        public decimal CurrentRepeats { get; set; } = BaseRepeats;

        public bool CreatedThroughForge { get; set; }

        public override CardExtraState DeepClone() => (BladeState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(CardCanonicalTags.SovereignBladeState);
            w.WriteDecimal(CurrentDamage);
            w.WriteDecimal(CurrentRepeats);
            w.WriteBool(CreatedThroughForge);
        }
    }
}
