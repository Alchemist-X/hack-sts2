namespace Sts2Sim.Core.Powers;

public sealed class SlowPower : PowerModel
{
    public const string PowerId = "SLOW_POWER";

    public const decimal SlowStep = 0.1m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int SlowAmount { get; private set; }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        SlowAmount++;
    }

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (target == null || !ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return 1m + SlowStep * SlowAmount;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        SlowAmount = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(SlowAmount);
    }
}
