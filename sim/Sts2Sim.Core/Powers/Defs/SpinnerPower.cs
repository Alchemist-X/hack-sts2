namespace Sts2Sim.Core.Powers;

public sealed class SpinnerPower : PowerModel
{
    public const string PowerId = "SPINNER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        for (int i = 0; i < Amount; i++)
        {
            OrbCmd.Channel<GlassOrb>(cs, player);
        }
    }
}
