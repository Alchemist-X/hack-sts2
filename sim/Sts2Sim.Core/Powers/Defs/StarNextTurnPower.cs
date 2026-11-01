namespace Sts2Sim.Core.Powers;

public sealed class StarNextTurnPower : PowerModel
{
    public const string PowerId = "STAR_NEXT_TURN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        PlayerCmd.GainStars(cs, Amount, player);
        PowerCmd.Remove(cs, this);
    }
}
