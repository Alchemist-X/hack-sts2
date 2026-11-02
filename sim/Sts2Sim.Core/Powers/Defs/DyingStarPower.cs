namespace Sts2Sim.Core.Powers;

public sealed class DyingStarPower : TemporaryStrengthPower
{
    public const string PowerId = "DYING_STAR_POWER";

    public const string OriginId = "DYING_STAR";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
