namespace Sts2Sim.Core.Powers;

public sealed class WellLaidPlansPower : PowerModel
{
    public const string PowerId = "WELL_LAID_PLANS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldFlush(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return false;
    }
}
