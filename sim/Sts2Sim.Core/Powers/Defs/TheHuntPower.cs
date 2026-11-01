namespace Sts2Sim.Core.Powers;

public sealed class TheHuntPower : PowerModel
{
    public const string PowerId = "THE_HUNT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;
}
