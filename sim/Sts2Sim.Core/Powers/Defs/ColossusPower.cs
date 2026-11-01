namespace Sts2Sim.Core.Powers;

public sealed class ColossusPower : PowerModel
{
    public const string PowerId = "COLOSSUS_POWER";

    public const decimal DamageDecrease = 0.5m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

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
        if (dealer == null)
        {
            return 1m;
        }
        if (!dealer.HasPower<VulnerablePower>())
        {
            return 1m;
        }
        return DamageDecrease;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
