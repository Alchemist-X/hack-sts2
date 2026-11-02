namespace Sts2Sim.Core.Powers;

public sealed class FeedingFrenzyPower : TemporaryStrengthPower
{
    public const string PowerId = "FEEDING_FRENZY_POWER";

    public const string OriginId = "FEEDING_FRENZY";

    public override string Id => PowerId;

    public override string OriginModelId => OriginId;
}
