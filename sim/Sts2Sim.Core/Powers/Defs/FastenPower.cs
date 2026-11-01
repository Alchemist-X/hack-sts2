namespace Sts2Sim.Core.Powers;

public sealed class FastenPower : PowerModel
{
    public const string PowerId = "FASTEN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, target))
        {
            return 0m;
        }
        if (!props.IsPoweredCardOrMonsterMoveBlock())
        {
            return 0m;
        }
        if (cardSource != null && !cardSource.HasTag(CardTag.Defend))
        {
            return 0m;
        }
        return Amount;
    }
}
