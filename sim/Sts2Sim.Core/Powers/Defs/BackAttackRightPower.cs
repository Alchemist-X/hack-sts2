namespace Sts2Sim.Core.Powers;

public sealed class BackAttackRightPower : PowerModel
{
    public const string PowerId = "BACK_ATTACK_RIGHT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;
}
