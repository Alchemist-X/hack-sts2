namespace Sts2Sim.Core.Powers;

public sealed class HailstormPower : PowerModel
{
    public const string PowerId = "HAILSTORM_POWER";

    public const string FrostOrbKey = "FrostOrbs";

    public const int FrostOrbCount = 1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's orb queue, but its owner is not a player");
        var orbs = player.OrbQueue.Orbs;
        int frost = 0;
        for (int i = 0; i < orbs.Count; i++)
        {
            if (orbs[i] is FrostOrb)
            {
                frost++;
            }
        }
        if (frost < FrostOrbCount)
        {
            return;
        }
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        CreatureCmd.Damage(cs, hittable, Amount, ValueProp.Unpowered, Owner);
    }
}
