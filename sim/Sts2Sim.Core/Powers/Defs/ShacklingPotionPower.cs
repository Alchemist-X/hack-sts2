namespace Sts2Sim.Core.Powers;

public sealed class ShacklingPotionPower : TemporaryStrengthPower
{
    public const string PowerId = "SHACKLING_POTION_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "SHACKLING_POTION";

    protected override bool IsPositive => false;
}
