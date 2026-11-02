namespace Sts2Sim.Core.Powers;

public sealed class SpeedPotionPower : TemporaryDexterityPower
{
    public const string PowerId = "SPEED_POTION_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "SPEED_POTION";
}
