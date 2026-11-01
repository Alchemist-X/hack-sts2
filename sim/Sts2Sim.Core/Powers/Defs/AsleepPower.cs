namespace Sts2Sim.Core.Powers;

public sealed class AsleepPower : PowerModel
{
    public const string PowerId = "ASLEEP_POWER";

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
        var plating = Owner.GetPower<PlatingPower>();
        if (plating != null)
        {
            PowerCmd.Remove(cs, plating);
        }
        CreatureCmd.StunWithMove(cs, (MonsterState)Owner, LagavulinMatriarch.WakeUpMove, LagavulinMatriarch.SlashMoveId);
        PowerCmd.Remove(cs, this);
    }

    public override void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Amount <= 1)
        {
            var plating = Owner.GetPower<PlatingPower>();
            if (plating != null)
            {
                PowerCmd.Remove(cs, plating);
            }
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
            LagavulinMatriarch.WakeUpMove(cs, (MonsterState)Owner, Array.Empty<CreatureState>());
        }
    }
}
