namespace Sts2Sim.Core.Monsters;

public sealed class FossilStalker : MonsterDef
{
    public const string MonsterIdEntry = "FOSSIL_STALKER";
    public const string TackleMoveId = "TACKLE_MOVE";
    public const string LatchMoveId = "LATCH_MOVE";
    public const string LashMoveId = "LASH_MOVE";
    public const string RandStateId = "RAND";

    public const int MinInitialHpBase = 51;
    public const int MinInitialHpTough = 54;
    public const int MaxInitialHpBase = 53;
    public const int MaxInitialHpTough = 56;
    public const int TackleDamageBase = 9;
    public const int TackleDamageDeadly = 11;
    public const int LatchDamageBase = 12;
    public const int LatchDamageDeadly = 14;
    public const int LashDamageBase = 3;
    public const int LashDamageDeadly = 4;
    public const int LashRepeat = 2;
    public const int TackleFrailAmount = 1;
    public const int SuckAmount = 3;

    public FossilStalker()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int TackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TackleDamageDeadly, TackleDamageBase);

    public static int LatchDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LatchDamageDeadly, LatchDamageBase);

    public static int LashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LashDamageDeadly, LashDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SuckPower>(cs, self, SuckAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var tackle = new MoveStateDef(TackleMoveId, TackleMove, new SingleAttackIntent(TackleDamage(cs)), new DebuffIntent());
        var latch = new MoveStateDef(LatchMoveId, LatchMove, new SingleAttackIntent(LatchDamage(cs)));
        var lash = new MoveStateDef(LashMoveId, LashAttack, new MultiAttackIntent(LashDamage(cs), LashRepeat));
        var rand = new RandomBranchStateDef(RandStateId);
        latch.FollowUpState = rand;
        tackle.FollowUpState = rand;
        lash.FollowUpState = rand;
        rand.AddBranch(latch, 2);
        rand.AddBranch(tackle, 2);
        rand.AddBranch(lash, 2);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, tackle, latch, lash }, latch);
    }

    private static void TackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TackleDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, TackleFrailAmount, self, null);
    }

    private static void LatchMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LatchDamage(cs)).FromMonster(self).Execute();
    }

    private static void LashAttack(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LashDamage(cs)).WithHitCount(LashRepeat).FromMonster(self).Execute();
    }
}
