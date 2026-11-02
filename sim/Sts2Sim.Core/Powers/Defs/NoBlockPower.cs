namespace Sts2Sim.Core.Powers;

public sealed class NoBlockPower : PowerModel
{
    public const string PowerId = "NO_BLOCK_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (props.HasProp(ValueProp.Unpowered))
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        return 0m;
    }
}
