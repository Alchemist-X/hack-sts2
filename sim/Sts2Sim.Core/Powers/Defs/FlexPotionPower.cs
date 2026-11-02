namespace Sts2Sim.Core.Powers;

public sealed class FlexPotionPower : TemporaryStrengthPower
{
    public const string PowerId = "FLEX_POTION_POWER";

    public const string OriginId = "FLEX_POTION";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;
}
