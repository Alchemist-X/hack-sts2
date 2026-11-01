namespace Sts2Sim.Core.Powers;

public sealed class SetupStrikePower : TemporaryStrengthPower
{
    public const string PowerId = "SETUP_STRIKE_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "SETUP_STRIKE";
}
