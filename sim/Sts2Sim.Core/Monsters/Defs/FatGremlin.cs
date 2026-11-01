namespace Sts2Sim.Core.Monsters;

public sealed class FatGremlin : MonsterDef
{
    public const string MonsterIdEntry = "FAT_GREMLIN";
    public const string SpawnedMoveId = "SPAWNED_MOVE";
    public const string FleeMoveId = "FLEE_MOVE";

    public const int MinInitialHpBase = 13;
    public const int MinInitialHpTough = 14;
    public const int MaxInitialHpBase = 17;
    public const int MaxInitialHpTough = 18;

    public FatGremlin()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var spawned = new MoveStateDef(SpawnedMoveId, SpawnedMove, Intents.Stun);
        var flee = new MoveStateDef(FleeMoveId, FleeMove, Intents.Escape);
        spawned.FollowUpState = flee;
        flee.FollowUpState = flee;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { spawned, flee }, spawned);
    }

    private static void SpawnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void FleeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.Escape(cs, self);
    }
}
