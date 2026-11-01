namespace Sts2Sim.Core.Powers;

public sealed class ImbalancedPower : PowerModel
{
    public const string PowerId = "IMBALANCED_POWER";

    public const string BowlbugRockId = "BOWLBUG_ROCK";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (!ReferenceEquals(dealer, Owner) || !result.WasFullyBlocked)
        {
            return;
        }
        if (!MonsterPowerCoupling.Is(Owner, BowlbugRockId, out var bowlbugRock))
        {
            CreatureCmd.Stun(cs, (MonsterState)Owner);
            return;
        }
        MonsterPowerCoupling.SetFlag(bowlbugRock, "IsOffBalance", true, PowerId);
    }
}
