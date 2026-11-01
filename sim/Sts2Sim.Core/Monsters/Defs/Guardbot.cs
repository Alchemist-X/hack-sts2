namespace Sts2Sim.Core.Monsters;

public sealed class Guardbot : MonsterDef
{
    public const string MonsterIdEntry = "GUARDBOT";
    public const string GuardMoveId = "GUARD_MOVE";
    public const string FabricatorMonsterId = "FABRICATOR";

    public const int MinInitialHpBase = 16;
    public const int MinInitialHpTough = 17;
    public const int MaxInitialHpBase = 20;
    public const int MaxInitialHpTough = 21;
    public const int GuardBlock = 15;

    public Guardbot()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var guard = new MoveStateDef(GuardMoveId, GuardMove, Intents.Defend);
        guard.FollowUpState = guard;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { guard }, guard);
    }

    private static void GuardMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var fabricators = new List<MonsterState>();
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (string.Equals(enemies[i].MonsterId, FabricatorMonsterId, StringComparison.Ordinal))
            {
                fabricators.Add(enemies[i]);
            }
        }
        for (int i = 0; i < fabricators.Count; i++)
        {
            CreatureCmd.GainBlock(cs, fabricators[i], GuardBlock, ValueProp.Unpowered, null);
        }
    }
}
