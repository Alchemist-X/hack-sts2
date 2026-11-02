namespace Sts2Sim.Core.Powers;

public sealed class LightningRodPower : PowerModel
{
    public const string PowerId = "LIGHTNING_ROD_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            OrbCmd.Channel<LightningOrb>(cs, player);
        }
        PowerCmd.Decrement(cs, this);
    }
}
