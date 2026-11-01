namespace Sts2Sim.Core.Powers;

public sealed class AsleepPower : PowerModel
{
    public const string PowerId = "ASLEEP_POWER";

    public const string LagavulinMatriarchId = "LAGAVULIN_MATRIARCH";

    public const string WakeUpNextMoveId = "SLASH_MOVE";

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
        if (Owner.GetPower<PlatingPower>() is { } plating)
        {
            PowerCmd.Remove(cs, plating);
        }
        var monster = MonsterPowerCoupling.Require(Owner, LagavulinMatriarchId, PowerId);
        MonsterPowerCoupling.SetFlag(monster, "IsAwake", true, PowerId);
        CreatureCmd.StunWithMove(cs, monster, MonsterPowerCoupling.Move(monster, "WakeUpMove", PowerId), WakeUpNextMoveId);
        PowerCmd.Remove(cs, this);
    }

    public override void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Amount <= 1 && Owner.GetPower<PlatingPower>() is { } plating)
        {
            PowerCmd.Remove(cs, plating);
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
            var monster = MonsterPowerCoupling.Require(Owner, LagavulinMatriarchId, PowerId);
            MonsterPowerCoupling.Call(cs, monster, "WakeUpMove", PowerId);
        }
    }
}
