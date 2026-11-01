namespace Sts2Sim.Core.Powers;

public sealed class EnergyNextTurnPower : PowerModel
{
    public const string PowerId = "ENERGY_NEXT_TURN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, Amount, player);
        PowerCmd.Remove(cs, this);
    }
}
