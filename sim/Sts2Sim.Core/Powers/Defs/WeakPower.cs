namespace Sts2Sim.Core.Powers;

public sealed class WeakPower : PowerModel
{
    public const string PowerId = "WEAK_POWER";

    public const decimal DamageDecrease = 0.75m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (dealer == null || !ReferenceEquals(dealer, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        var cs = rs.Combat!;
        decimal num = DamageDecrease;
        if (target != null && MultiplierModifiers.FirstWeakRelic(target as PlayerState) is { } relic)
        {
            num = relic.ModifyWeakMultiplier(cs, target, num, props, dealer, cardSource);
        }
        if (dealer.GetPower(MultiplierModifierIds.DebilitatePower) is IWeakMultiplierModifier debilitate)
        {
            num = debilitate.ModifyWeakMultiplier(cs, dealer, num, props, dealer, cardSource);
        }
        return num;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.TickDownDuration(cs, this);
        }
    }
}
