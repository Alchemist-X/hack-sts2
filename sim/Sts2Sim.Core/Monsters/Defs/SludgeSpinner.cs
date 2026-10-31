namespace Sts2Sim.Core.Monsters;

public sealed class SludgeSpinner : MonsterDef
{
    public const string MonsterIdEntry = "SLUDGE_SPINNER";
    public const string RandStateId = "RAND";
    public const string OilSprayMoveId = "OIL_SPRAY_MOVE";
    public const string SlamMoveId = "SLAM_MOVE";
    public const string RageMoveId = "RAGE_MOVE";

    public const int MinInitialHpBase = 37;
    public const int MinInitialHpTough = 41;
    public const int MaxInitialHpBase = 39;
    public const int MaxInitialHpTough = 42;
    public const int OilSprayDamageBase = 8;
    public const int OilSprayDamageDeadly = 9;
    public const int SlamDamageBase = 11;
    public const int SlamDamageDeadly = 12;
    public const int RageDamageBase = 6;
    public const int RageDamageDeadly = 7;
    public const int OilSprayWeakAmount = 1;
    public const int RageStrengthAmount = 3;

    public SludgeSpinner()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int OilSprayDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, OilSprayDamageDeadly, OilSprayDamageBase);

    public static int SlamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SlamDamageDeadly, SlamDamageBase);

    public static int RageDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RageDamageDeadly, RageDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var oilSpray = new MoveStateDef(OilSprayMoveId, OilSprayMove, new SingleAttackIntent(OilSprayDamage(cs)), new DebuffIntent());
        var slam = new MoveStateDef(SlamMoveId, SlamMove, new SingleAttackIntent(SlamDamage(cs)));
        var rage = new MoveStateDef(RageMoveId, RageMove, new SingleAttackIntent(RageDamage(cs)), Intents.Buff);
        var rand = new RandomBranchStateDef(RandStateId);
        oilSpray.FollowUpState = rand;
        slam.FollowUpState = rand;
        rage.FollowUpState = rand;
        rand.AddBranch(oilSpray, MoveRepeatType.CannotRepeat);
        rand.AddBranch(slam, MoveRepeatType.CannotRepeat);
        rand.AddBranch(rage, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, oilSpray, slam, rage }, oilSpray);
    }

    private static void OilSprayMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, OilSprayDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, OilSprayWeakAmount, self, null);
    }

    private static void SlamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SlamDamage(cs)).FromMonster(self).Execute();
    }

    private static void RageMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RageDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, RageStrengthAmount, self, null);
    }
}
