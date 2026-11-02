namespace Sts2Sim.Core.Powers;

public sealed class ParryPower : PowerModel
{
    public const string PowerId = "PARRY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;
}
