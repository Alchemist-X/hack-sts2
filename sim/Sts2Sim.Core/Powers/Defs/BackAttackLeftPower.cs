namespace Sts2Sim.Core.Powers;

public sealed class BackAttackLeftPower : PowerModel
{
    public const string PowerId = "BACK_ATTACK_LEFT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;
}
