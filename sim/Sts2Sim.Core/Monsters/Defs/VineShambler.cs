namespace Sts2Sim.Core.Monsters;

public sealed class VineShambler : MonsterDef
{
    public const string MonsterIdEntry = "VINE_SHAMBLER";
    public const string GraspingVinesMoveId = "GRASPING_VINES_MOVE";
    public const string SwipeMoveId = "SWIPE_MOVE";
    public const string ChompMoveId = "CHOMP_MOVE";

    public const int HpBase = 61;
    public const int HpTough = 64;
    public const int GraspingVinesDamageBase = 8;
    public const int GraspingVinesDamageDeadly = 9;
    public const int SwipeDamageBase = 6;
    public const int SwipeDamageDeadly = 7;
    public const int SwipeRepeat = 2;
    public const int ChompDamageBase = 16;
    public const int ChompDamageDeadly = 18;
    public const int TangledAmount = 1;

    public VineShambler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int GraspingVinesDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GraspingVinesDamageDeadly, GraspingVinesDamageBase);

    public static int SwipeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwipeDamageDeadly, SwipeDamageBase);

    public static int ChompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChompDamageDeadly, ChompDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var graspingVines = new MoveStateDef(GraspingVinesMoveId, GraspingVinesMove, new SingleAttackIntent(GraspingVinesDamage(cs)), Intents.CardDebuff);
        var swipe = new MoveStateDef(SwipeMoveId, SwipeMove, new MultiAttackIntent(SwipeDamage(cs), SwipeRepeat));
        var chomp = new MoveStateDef(ChompMoveId, ChompMove, new SingleAttackIntent(ChompDamage(cs)));
        swipe.FollowUpState = graspingVines;
        graspingVines.FollowUpState = chomp;
        chomp.FollowUpState = swipe;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { graspingVines, swipe, chomp }, swipe);
    }

    private static void GraspingVinesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GraspingVinesDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<TangledPower>(cs, targets, TangledAmount, self, null);
    }

    private static void SwipeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwipeDamage(cs)).WithHitCount(SwipeRepeat).FromMonster(self).Execute();
    }

    private static void ChompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ChompDamage(cs)).FromMonster(self).Execute();
    }
}
