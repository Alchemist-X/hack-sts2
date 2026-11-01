namespace Sts2Sim.Core.Powers;

public sealed class DarkShacklesPower : TemporaryStrengthPower
{
    public const string PowerId = "DARK_SHACKLES_POWER";

    public const string OriginId = "DARK_SHACKLES";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;

    protected override bool IsPositive => false;
}
