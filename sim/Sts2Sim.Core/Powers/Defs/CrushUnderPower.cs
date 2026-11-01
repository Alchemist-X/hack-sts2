namespace Sts2Sim.Core.Powers;

public sealed class CrushUnderPower : TemporaryStrengthPower
{
    public const string PowerId = "CRUSH_UNDER_POWER";

    public const string OriginId = "CRUSH_UNDER";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
