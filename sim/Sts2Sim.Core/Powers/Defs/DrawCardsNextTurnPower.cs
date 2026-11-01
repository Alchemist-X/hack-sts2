namespace Sts2Sim.Core.Powers;

public sealed class DrawCardsNextTurnPower : PowerModel
{
    public const string PowerId = "DRAW_CARDS_NEXT_TURN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return count;
        }
        if (AmountOnTurnStart == 0)
        {
            return count;
        }
        return count + Amount;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && AmountOnTurnStart != 0)
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
