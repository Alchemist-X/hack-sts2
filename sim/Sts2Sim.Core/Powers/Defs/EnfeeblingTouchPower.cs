namespace Sts2Sim.Core.Powers;

public sealed class EnfeeblingTouchPower : TemporaryStrengthPower
{
    public const string PowerId = "ENFEEBLING_TOUCH_POWER";

    public const string OriginId = "ENFEEBLING_TOUCH";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
