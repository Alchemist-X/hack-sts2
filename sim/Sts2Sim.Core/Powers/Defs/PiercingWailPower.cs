namespace Sts2Sim.Core.Powers;

public sealed class PiercingWailPower : TemporaryStrengthPower
{
    public const string PowerId = "PIERCING_WAIL_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "PIERCING_WAIL";

    protected override bool IsPositive => false;
}
