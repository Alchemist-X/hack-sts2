namespace Sts2Sim.Core.Powers;

public sealed class ManglePower : TemporaryStrengthPower
{
    public const string PowerId = "MANGLE_POWER";

    public const string OriginId = "MANGLE";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
