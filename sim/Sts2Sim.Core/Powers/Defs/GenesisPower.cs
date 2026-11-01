namespace Sts2Sim.Core.Powers;

public sealed class GenesisPower : PowerModel
{
    public const string PowerId = "GENESIS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner as PlayerState))
        {
            PlayerCmd.GainStars(cs, Amount, player);
        }
    }
}
