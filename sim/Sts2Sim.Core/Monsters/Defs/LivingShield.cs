namespace Sts2Sim.Core.Monsters;

public sealed class LivingShield : MonsterDef
{
    public const string MonsterIdEntry = "LIVING_SHIELD";
    public const string ShieldSlamMoveId = "SHIELD_SLAM_MOVE";
    public const string ShieldSlamBranchId = "SHIELD_SLAM_BRANCH";
    public const string SmashMoveId = "SMASH_MOVE";

    public const int InitialHpBase = 55;
    public const int InitialHpTough = 65;
    public const int ShieldSlamDamage = 6;
    public const int SmashDamageBase = 16;
    public const int SmashDamageDeadly = 18;
    public const int EnrageStr = 3;
    public const int RampartAmount = 25;

    public LivingShield()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SmashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SmashDamageDeadly, SmashDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<RampartPower>(cs, self, RampartAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var shieldSlam = new MoveStateDef(ShieldSlamMoveId, ShieldSlamMove, new SingleAttackIntent(ShieldSlamDamage));
        var branch = new ConditionalBranchStateDef(ShieldSlamBranchId);
        var smash = new MoveStateDef(SmashMoveId, SmashMove, new SingleAttackIntent(SmashDamage(cs)), Intents.Buff);
        shieldSlam.FollowUpState = branch;
        branch.AddState(shieldSlam, static (c, m) => GetAllyCount(c, m) > 0);
        branch.AddState(smash, static (c, m) => GetAllyCount(c, m) == 0);
        smash.FollowUpState = smash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { shieldSlam, smash, branch }, shieldSlam);
    }

    private static void ShieldSlamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ShieldSlamDamage).FromMonster(self).Execute();
    }

    private static void SmashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SmashDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, EnrageStr, self, null);
    }

    private static int GetAllyCount(CombatState cs, MonsterState self)
    {
        var teammates = cs.GetTeammatesOf(self);
        int count = 0;
        for (int i = 0; i < teammates.Count; i++)
        {
            if (teammates[i].IsAlive && !ReferenceEquals(teammates[i], self))
            {
                count++;
            }
        }
        return count;
    }
}
