namespace Sts2Sim.Core.Powers;

public sealed class HotfixPower : TemporaryFocusPower
{
    public const string PowerId = "HOTFIX_POWER";

    public override string Id => PowerId;

    public override string OriginModelId => "HOTFIX";
}
