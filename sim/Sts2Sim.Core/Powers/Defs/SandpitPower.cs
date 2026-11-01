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
        var target = GetTarget(cs) ?? throw new InvalidOperationException($"{PowerId} has no target");
        if (oldOwner.IsDead || target.IsDead)
        {
            return;
        }
        var affected = AllAffectedCreatures(cs, target);
        for (int i = 0; i < affected.Count; i++)
        {
            var creature = affected[i];
            if (!creature.IsPlayer && !(creature is MonsterState { MonsterId: CombatStatus.OstyMonsterId }))
            {
                continue;
            }
            CreatureCmd.Kill(cs, creature, force: true);
        }
    }

    private static List<CreatureState> AllAffectedCreatures(CombatState cs, CreatureState target)
    {
        var player = target as PlayerState ?? throw new InvalidOperationException($"{PowerId}: target is not a player (Models.Powers/SandpitPower.cs:703)");
        var result = new List<CreatureState> { player };
        var pets = new List<MonsterState>();
        MonsterLifecycle.GetPets(cs, player, pets);
        result.AddRange(pets);
        return result;
    }
}
