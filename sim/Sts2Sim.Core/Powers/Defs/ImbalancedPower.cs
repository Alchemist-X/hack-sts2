namespace Sts2Sim.Core.Powers;

public sealed class ImbalancedPower : PowerModel
{
    public const string PowerId = "IMBALANCED_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (!ReferenceEquals(dealer, Owner) || !result.WasFullyBlocked)
        {
            return;
        }
        if (Owner is MonsterState monster && monster.Def is BowlbugRock)
        {
            BowlbugRock.GetExtra(monster).IsOffBalance = true;
            return;
        }
        if (Owner is MonsterState other)
        {
            CreatureCmd.Stun(cs, other);
        }
    }
}
