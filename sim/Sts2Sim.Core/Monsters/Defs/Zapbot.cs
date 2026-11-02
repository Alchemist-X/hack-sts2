namespace Sts2Sim.Core.Monsters;

public sealed class Zapbot : MonsterDef
{
    public const string MonsterIdEntry = "ZAPBOT";
    public const string ZapMoveId = "ZAP";

    public const int MinInitialHpBase = 18;
    public const int MinInitialHpTough = 19;
    public const int MaxInitialHpBase = 23;
    public const int MaxInitialHpTough = 24;
    public const int ZapDamageBase = 14;
    public const int ZapDamageDeadly = 15;
    public const int HighVoltageAmount = 2;

    public Zapbot()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ZapDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ZapDamageDeadly, ZapDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply(cs, HighVoltagePower.PowerId, self, HighVoltageAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var zap = new MoveStateDef(ZapMoveId, ZapMove, new SingleAttackIntent(ZapDamage(cs)));
        zap.FollowUpState = zap;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { zap }, zap);
    }

    private static void ZapMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ZapDamage(cs)).FromMonster(self).Execute();
    }
}
