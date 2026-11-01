namespace Sts2Sim.Core.Monsters;

public sealed class Fogmog : MonsterDef
{
    public const string MonsterIdEntry = "FOGMOG";
    public const string IllusionMoveId = "ILLUSION_MOVE";
    public const string SwipeMoveId = "SWIPE_MOVE";
    public const string SwipeRandomMoveId = "SWIPE_RANDOM_MOVE";
    public const string HeadbuttMoveId = "HEADBUTT_MOVE";
    public const string BranchMoveId = "BRANCH";
    public const string IllusionSlot = "illusion";

    public const int HpBase = 74;
    public const int HpTough = 78;
    public const int SwipeDamageBase = 8;
    public const int SwipeDamageDeadly = 9;
    public const int HeadbuttDamageBase = 14;
    public const int HeadbuttDamageDeadly = 16;
    public const int SwipeStrength = 1;
    public const float SwipeRandomWeight = 0.4f;
    public const float HeadbuttWeight = 0.6f;

    public Fogmog()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SwipeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwipeDamageDeadly, SwipeDamageBase);

    public static int HeadbuttDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HeadbuttDamageDeadly, HeadbuttDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var illusion = new MoveStateDef(IllusionMoveId, IllusionMove, Intents.Summon);
        var swipe = new MoveStateDef(SwipeMoveId, SwipeMove, new SingleAttackIntent(SwipeDamage(cs)), Intents.Buff);
        var swipeRandom = new MoveStateDef(SwipeRandomMoveId, SwipeMove, new SingleAttackIntent(SwipeDamage(cs)), Intents.Buff);
        var headbutt = new MoveStateDef(HeadbuttMoveId, HeadbuttMove, new SingleAttackIntent(HeadbuttDamage(cs)));
        var branch = new RandomBranchStateDef(BranchMoveId);
        branch.AddBranch(swipeRandom, MoveRepeatType.CannotRepeat, SwipeRandomWeight);
        branch.AddBranch(headbutt, MoveRepeatType.CannotRepeat, HeadbuttWeight);
        illusion.FollowUpState = swipe;
        swipe.FollowUpState = branch;
        swipeRandom.FollowUpState = headbutt;
        headbutt.FollowUpState = swipe;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { illusion, swipe, swipeRandom, branch, headbutt }, illusion);
    }

    private static void IllusionMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        if (cs.IsInProgress)
        {
            CreatureCmd.Add(cs, MonsterRegistry.Get(EyeWithTeeth.MonsterIdEntry), CombatSide.Enemy, IllusionSlot);
        }
    }

    private static void SwipeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwipeDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, SwipeStrength, self, null);
    }

    private static void HeadbuttMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HeadbuttDamage(cs)).FromMonster(self).Execute();
    }
}
