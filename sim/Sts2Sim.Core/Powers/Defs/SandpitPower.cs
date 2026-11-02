namespace Sts2Sim.Core.Powers;

public sealed class SandpitPower : PowerModel
{
    public const string PowerId = "SANDPIT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override void AfterSideTurnStartLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        var target = GetTarget(cs) ?? throw new InvalidOperationException($"{PowerId} has no target creature {TargetId}");
        if (oldOwner.IsDead || target.IsDead)
        {
            return;
        }
        var affected = AllAffectedCreatures(cs, target);
        for (int i = 0; i < affected.Count; i++)
        {
            var current = affected[i];
            if (!current.IsPlayer && !(current is MonsterState { MonsterId: PlayerState.OstyMonsterId }))
            {
                continue;
            }
            CreatureCmd.Kill(cs, current, force: true);
        }
    }

    private static List<CreatureState> AllAffectedCreatures(CombatState cs, CreatureState target)
    {
        var player = target as PlayerState ?? CombatStatus.PetOwnerOf(cs, target)
            ?? throw new InvalidOperationException($"{PowerId} target {target.CombatId} has no player");
        var pets = new List<MonsterState>();
        MonsterLifecycle.GetPets(cs, player, pets);
        var result = new List<CreatureState>(1 + pets.Count) { player };
        result.AddRange(pets);
        return result;
    }
}
