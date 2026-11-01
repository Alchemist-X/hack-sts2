namespace Sts2Sim.Core.Powers;

public sealed class AccelerantPower : PowerModel
{
    public const string PowerId = "ACCELERANT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;
}
