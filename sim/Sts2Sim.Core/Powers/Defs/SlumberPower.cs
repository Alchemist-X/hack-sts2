namespace Sts2Sim.Core.Powers;

public sealed class SlumberPower : PowerModel
{
    public const string PowerId = "SLUMBER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage == 0)
        {
            return;
        }
        var cs = rs.Combat!;
        PowerCmd.Decrement(cs, this);
        if (Amount <= 0)
        {
            CreatureCmd.StunWithMove(cs, (MonsterState)Owner, SlumberingBeetle.WakeUpMove, SlumberingBeetle.RolloutMoveId);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Decrement(cs, this);
        if (Amount <= 0)
        {
            SlumberingBeetle.WakeUpMove(cs, (MonsterState)Owner, Array.Empty<CreatureState>());
        }
    }
}
