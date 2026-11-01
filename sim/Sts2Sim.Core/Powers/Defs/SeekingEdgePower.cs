namespace Sts2Sim.Core.Powers;

public sealed class SeekingEdgePower : PowerModel
{
    public const string PowerId = "SEEKING_EDGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;
}
