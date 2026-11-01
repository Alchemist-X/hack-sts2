namespace Sts2Sim.Core.Powers;

public sealed class HardToKillPower : PowerModel
{
    public const string PowerId = "HARD_TO_KILL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return decimal.MaxValue;
        }
        return Amount;
    }
}
