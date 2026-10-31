namespace Sts2Sim.Core.Monsters;

public sealed class ShrinkerBeetle : MonsterDef
{
    public const string MonsterIdEntry = "SHRINKER_BEETLE";
    public const string ShrinkerMoveId = "SHRINKER_MOVE";
    public const string ChompMoveId = "CHOMP_MOVE";
    public const string StompMoveId = "STOMP_MOVE";

    public const int MinInitialHpBase = 38;
    public const int MinInitialHpTough = 40;
    public const int MaxInitialHpBase = 40;
    public const int MaxInitialHpTough = 42;
    public const int ChompDamageBase = 7;
    public const int ChompDamageDeadly = 8;
    public const int StompDamageBase = 13;
    public const int StompDamageDeadly = 14;
    public const int ShrinkAmount = -1;

    public ShrinkerBeetle()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ChompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChompDamageDeadly, ChompDamageBase);

    public static int StompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StompDamageDeadly, StompDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var shrinker = new MoveStateDef(ShrinkerMoveId, ShrinkMove, new DebuffIntent(strong: true));
        var chomp = new MoveStateDef(ChompMoveId, ChompMove, new SingleAttackIntent(ChompDamage(cs)));
        var stomp = new MoveStateDef(StompMoveId, StompMove, new SingleAttackIntent(StompDamage(cs)));
        shrinker.FollowUpState = chomp;
        chomp.FollowUpState = stomp;
        stomp.FollowUpState = chomp;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { shrinker, chomp, stomp }, shrinker);
    }

    private static void ShrinkMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ShrinkPower>(cs, targets, ShrinkAmount, self, null);
    }

    private static void ChompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ChompDamage(cs)).FromMonster(self).Execute();
    }

    private static void StompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StompDamage(cs)).FromMonster(self).Execute();
    }
}
