namespace Sts2Sim.Core.Powers;

public sealed class SlumberPower : PowerModel
{
    public const string PowerId = "SLUMBER_POWER";

    public const string SlumberingBeetleId = "SLUMBERING_BEETLE";

    public const string WakeUpNextMoveId = "ROLL_OUT_MOVE";

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
            var beetle = MonsterPowerCoupling.Require(Owner, SlumberingBeetleId, PowerId);
            CreatureCmd.StunWithMove(cs, beetle, MonsterPowerCoupling.Move(beetle, "WakeUpMove", PowerId), WakeUpNextMoveId);
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
            var beetle = MonsterPowerCoupling.Require(Owner, SlumberingBeetleId, PowerId);
            MonsterPowerCoupling.Call(cs, beetle, "WakeUpMove", PowerId);
        }
    }
}
