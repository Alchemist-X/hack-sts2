namespace Sts2Sim.Core.Powers;

public sealed class FrailPower : PowerModel
{
    public const string PowerId = "FRAIL_POWER";

    public const decimal BlockMultiplier = 0.75m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, target))
        {
            return 1m;
        }
        if (!props.IsPoweredCardOrMonsterMoveBlock())
        {
            return 1m;
        }
        return BlockMultiplier;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.TickDownDuration(cs, this);
        }
    }
}
