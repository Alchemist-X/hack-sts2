namespace Sts2Sim.Core.Powers;

public sealed class SlowPower : PowerModel
{
    public const string PowerId = "SLOW_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal SlowAmount { get; private set; }

    public decimal DisplayAmount { get; private set; }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        SlowAmount += 1m;
        DisplayAmount = SlowAmount * 10m;
    }

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return 1m + 0.1m * SlowAmount;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        SlowAmount = 0m;
        DisplayAmount = 0m;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(SlowAmount);
        w.WriteDecimal(DisplayAmount);
    }
}
