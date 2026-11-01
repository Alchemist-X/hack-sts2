namespace Sts2Sim.Core.Powers;

public sealed class ClarityPower : PowerModel
{
    public const string PowerId = "CLARITY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return count;
        }
        return count + 1m;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
