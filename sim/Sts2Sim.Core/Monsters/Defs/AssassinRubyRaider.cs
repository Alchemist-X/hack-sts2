namespace Sts2Sim.Core.Monsters;

public sealed class AssassinRubyRaider : MonsterDef
{
    public const string MonsterIdEntry = "ASSASSIN_RUBY_RAIDER";
    public const string KillshotMoveId = "KILLSHOT_MOVE";

    public const int MinInitialHpBase = 18;
    public const int MinInitialHpTough = 19;
    public const int MaxInitialHpBase = 23;
    public const int MaxInitialHpTough = 24;
    public const int KillshotDamageBase = 10;
    public const int KillshotDamageDeadly = 11;

    public AssassinRubyRaider()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int KillshotDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, KillshotDamageDeadly, KillshotDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var killshot = new MoveStateDef(KillshotMoveId, KillshotMove, new SingleAttackIntent(KillshotDamage(cs)));
        killshot.FollowUpState = killshot;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { killshot }, killshot);
    }

    private static void KillshotMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, KillshotDamage(cs)).FromMonster(self).Execute();
    }
}
