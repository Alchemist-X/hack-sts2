namespace Sts2Sim.Core.Powers;

public sealed class MonarchsGazeStrengthDownPower : TemporaryStrengthPower
{
    public const string PowerId = "MONARCHS_GAZE_STRENGTH_DOWN_POWER";

    public const string OriginId = "MONARCHS_GAZE";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
