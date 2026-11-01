namespace Sts2Sim.Core.Powers;

public sealed class FanOfKnivesPower : PowerModel
{
    public const string PowerId = "FAN_OF_KNIVES_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;
}
