namespace Sts2Sim.Core.Powers;

public sealed class SynchronizePower : TemporaryFocusPower
{
    public const string PowerId = "SYNCHRONIZE_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "SYNCHRONIZE";
}
