namespace Sts2Sim.Core.Monsters;

public sealed class LeafSlimeS : MonsterDef
{
    public const string MonsterIdEntry = "LEAF_SLIME_S";
    public const string TackleMoveId = "TACKLE_MOVE";
    public const string GoopMoveId = "GOOP_MOVE";
    public const string RandId = "RAND";

    public const int MinInitialHpBase = 11;
    public const int MinInitialHpTough = 12;
    public const int MaxInitialHpBase = 15;
    public const int MaxInitialHpTough = 16;
    public const int TackleDamageBase = 3;
    public const int TackleDamageDeadly = 4;
    public const int GoopAmount = 1;

    public LeafSlimeS()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int TackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TackleDamageDeadly, TackleDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var tackle = new MoveStateDef(TackleMoveId, TackleMove, new SingleAttackIntent(TackleDamage(cs)));
        var goop = new MoveStateDef(GoopMoveId, GoopMove, new StatusIntent(GoopAmount));
        var rand = new RandomBranchStateDef(RandId);
        tackle.FollowUpState = rand;
        goop.FollowUpState = rand;
        rand.AddBranch(tackle, MoveRepeatType.CannotRepeat);
        rand.AddBranch(goop, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { tackle, goop, rand }, rand);
    }

    private static void TackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TackleDamage(cs)).FromMonster(self).Execute();
    }

    private static void GoopMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, GoopAmount, null);
    }
}
