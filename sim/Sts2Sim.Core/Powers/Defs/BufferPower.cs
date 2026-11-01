namespace Sts2Sim.Core.Powers;

public sealed class BufferPower : PowerModel
{
    public const string PowerId = "BUFFER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHpLostAfterOstyLate(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        return 0m;
    }

    public override void AfterModifyingHpLostAfterOsty(RunState rs)
    {
        PowerCmd.Decrement(rs.Combat!, this);
    }
}
