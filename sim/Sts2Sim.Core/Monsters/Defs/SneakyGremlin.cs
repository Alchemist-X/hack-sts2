namespace Sts2Sim.Core.Monsters;

public sealed class SneakyGremlin : MonsterDef
{
    public const string MonsterIdEntry = "SNEAKY_GREMLIN";
    public const string SpawnedMoveId = "SPAWNED_MOVE";
    public const string TackleMoveId = "TACKLE_MOVE";

    public const int MinInitialHpBase = 10;
    public const int MinInitialHpTough = 11;
    public const int MaxInitialHpBase = 14;
    public const int MaxInitialHpTough = 15;
    public const int TackleDamageBase = 9;
    public const int TackleDamageDeadly = 10;

    public SneakyGremlin()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int TackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TackleDamageDeadly, TackleDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var spawned = new MoveStateDef(SpawnedMoveId, SpawnedMove, Intents.Stun);
        var tackle = new MoveStateDef(TackleMoveId, TackleMove, new SingleAttackIntent(TackleDamage(cs)));
        spawned.FollowUpState = tackle;
        tackle.FollowUpState = tackle;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { spawned, tackle }, spawned);
    }

    private static void SpawnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void TackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TackleDamage(cs)).FromMonster(self).Execute();
    }
}
