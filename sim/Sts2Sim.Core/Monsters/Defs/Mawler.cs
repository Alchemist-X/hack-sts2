namespace Sts2Sim.Core.Monsters;

public sealed class Mawler : MonsterDef
{
    public const string MonsterIdEntry = "MAWLER";
    public const string RipAndTearMoveId = "RIP_AND_TEAR_MOVE";
    public const string RoarMoveId = "ROAR_MOVE";
    public const string ClawMoveId = "CLAW_MOVE";
    public const string RandStateId = "RAND";

    public const int InitialHpBase = 72;
    public const int InitialHpTough = 76;
    public const int RipAndTearDamageBase = 14;
    public const int RipAndTearDamageDeadly = 16;
    public const int ClawDamageBase = 4;
    public const int ClawDamageDeadly = 5;
    public const int ClawRepeat = 2;
    public const int RoarVulnerable = 3;

    public Mawler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int RipAndTearDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RipAndTearDamageDeadly, RipAndTearDamageBase);

    public static int ClawDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ClawDamageDeadly, ClawDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var ripAndTear = new MoveStateDef(RipAndTearMoveId, RipAndTearMove, new SingleAttackIntent(RipAndTearDamage(cs)));
        var roar = new MoveStateDef(RoarMoveId, RoarMove, new DebuffIntent());
        var claw = new MoveStateDef(ClawMoveId, ClawMove, new MultiAttackIntent(ClawDamage(cs), ClawRepeat));
        var rand = new RandomBranchStateDef(RandStateId);
        ripAndTear.FollowUpState = rand;
        roar.FollowUpState = rand;
        claw.FollowUpState = rand;
        rand.AddBranch(ripAndTear, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(roar, MoveRepeatType.UseOnlyOnce, 1f);
        rand.AddBranch(claw, MoveRepeatType.CannotRepeat, 1f);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { ripAndTear, roar, claw, rand }, claw);
    }

    private static void ClawMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ClawDamage(cs)).WithHitCount(ClawRepeat).FromMonster(self).Execute();
    }

    private static void RipAndTearMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RipAndTearDamage(cs)).FromMonster(self).Execute();
    }

    private static void RoarMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<VulnerablePower>(cs, targets, RoarVulnerable, self, null);
    }
}
