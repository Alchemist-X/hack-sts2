namespace Sts2Sim.Core.Powers;

public sealed class LoopPower : PowerModel
{
    public const string PowerId = "LOOP_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState) || player.OrbQueue.Orbs.Count == 0)
        {
            return;
        }
        for (int i = 0; i < Amount; i++)
        {
            OrbCmd.Passive(cs, player.OrbQueue.Orbs[0], null);
        }
    }
}
