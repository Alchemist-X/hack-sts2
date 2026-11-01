namespace Sts2Sim.Core.Powers;

public sealed class AnticipatePower : TemporaryDexterityPower
{
    public const string PowerId = "ANTICIPATE_POWER";

    public const string OriginId = "ANTICIPATE";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;
}
