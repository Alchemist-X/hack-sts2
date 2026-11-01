namespace Sts2Sim.Core.Powers;

public sealed class ConsumingShadowPower : PowerModel
{
    public const string PowerId = "CONSUMING_SHADOW_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's orb queue, but its owner is not a player");
        if (player.OrbQueue.Orbs.Count == 0)
        {
            return;
        }
        for (int i = 0; i < Amount; i++)
        {
            OrbCmd.EvokeLast(cs, player);
        }
    }
}
