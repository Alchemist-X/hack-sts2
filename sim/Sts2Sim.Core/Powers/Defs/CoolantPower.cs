namespace Sts2Sim.Core.Powers;

public sealed class CoolantPower : PowerModel
{
    public const string PowerId = "COOLANT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's orb queue, but its owner is not a player");
        var orbs = player.OrbQueue.Orbs;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < orbs.Count; i++)
        {
            distinct.Add(orbs[i].Id);
        }
        CreatureCmd.GainBlock(cs, Owner, distinct.Count * Amount, ValueProp.Unpowered, null);
    }
}
