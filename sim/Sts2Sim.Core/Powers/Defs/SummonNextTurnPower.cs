namespace Sts2Sim.Core.Powers;

public sealed class SummonNextTurnPower : PowerModel
{
    public const string PowerId = "SUMMON_NEXT_TURN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState) || AmountOnTurnStart == 0)
        {
            return;
        }
        OstyCmd.Summon(cs, player, Amount, this);
        PowerCmd.Remove(cs, this);
    }
}
