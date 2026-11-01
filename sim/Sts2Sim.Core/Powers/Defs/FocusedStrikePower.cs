namespace Sts2Sim.Core.Powers;

public sealed class FocusedStrikePower : TemporaryFocusPower
{
    public const string PowerId = "FOCUSED_STRIKE_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "FOCUSED_STRIKE";
}
