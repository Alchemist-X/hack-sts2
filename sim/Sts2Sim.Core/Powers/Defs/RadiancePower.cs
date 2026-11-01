namespace Sts2Sim.Core.Powers;

public sealed class RadiancePower : PowerModel
{
    public const string PowerId = "RADIANCE_POWER";

    public const int Energy = 1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, Energy, player);
        PowerCmd.Decrement(cs, this);
    }
}
