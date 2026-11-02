namespace Sts2Sim.Core.Powers;

public sealed class MinionPower : PowerModel
{
    public const string PowerId = "MINION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool OwnerIsSecondaryEnemy => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    public override bool ShouldOwnerDeathTriggerFatal() => false;
}
