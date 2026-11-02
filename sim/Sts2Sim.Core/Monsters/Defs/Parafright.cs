namespace Sts2Sim.Core.Monsters;

public sealed class Parafright : MonsterDef
{
    public const string MonsterIdEntry = "PARAFRIGHT";
    public const string SlamMoveId = "SLAM_MOVE";
    public const string IllusionPowerId = "ILLUSION_POWER";

    public const int Hp = 21;
    public const int SlamDamageBase = 16;
    public const int SlamDamageDeadly = 17;
    public const int IllusionAmount = 1;

    public Parafright()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => Hp;

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SlamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SlamDamageDeadly, SlamDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply(cs, IllusionPowerId, self, IllusionAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var slam = new MoveStateDef(SlamMoveId, SlamMove, new SingleAttackIntent(SlamDamage(cs)));
        slam.FollowUpState = slam;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { slam }, slam);
    }

    private static void SlamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SlamDamage(cs)).FromMonster(self).Execute();
    }
}
