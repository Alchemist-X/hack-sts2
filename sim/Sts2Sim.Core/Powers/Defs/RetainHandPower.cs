namespace Sts2Sim.Core.Powers;

public sealed class RetainHandPower : PowerModel
{
    public const string PowerId = "RETAIN_HAND_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldFlush(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return false;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
