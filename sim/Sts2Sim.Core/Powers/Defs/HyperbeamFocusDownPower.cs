namespace Sts2Sim.Core.Powers;

public sealed class HyperbeamFocusDownPower : TemporaryFocusPower
{
    public const string PowerId = "HYPERBEAM_FOCUS_DOWN_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "HYPERBEAM";

    protected override bool IsPositive => false;
}
