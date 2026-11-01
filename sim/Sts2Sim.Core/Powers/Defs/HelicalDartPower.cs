namespace Sts2Sim.Core.Powers;

public sealed class HelicalDartPower : TemporaryDexterityPower
{
    public const string PowerId = "HELICAL_DART_POWER";

    public const string OriginId = "HELICAL_DART";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;
}
