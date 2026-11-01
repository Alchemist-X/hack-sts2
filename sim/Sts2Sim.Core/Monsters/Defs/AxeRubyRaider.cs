namespace Sts2Sim.Core.Monsters;

public sealed class AxeRubyRaider : MonsterDef
{
    public const string MonsterIdEntry = "AXE_RUBY_RAIDER";
    public const string Swing1MoveId = "SWING_1";
    public const string Swing2MoveId = "SWING_2";
    public const string BigSwingMoveId = "BIG_SWING";

    public const int MinInitialHpBase = 20;
    public const int MinInitialHpTough = 21;
    public const int MaxInitialHpBase = 22;
    public const int MaxInitialHpTough = 23;
    public const int SwingDamageBase = 5;
    public const int SwingDamageDeadly = 6;
    public const int SwingBlockBase = 5;
    public const int SwingBlockDeadly = 6;
    public const int BigSwingDamageBase = 12;
    public const int BigSwingDamageDeadly = 13;

    public AxeRubyRaider()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SwingDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwingDamageDeadly, SwingDamageBase);

    public static int SwingBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwingBlockDeadly, SwingBlockBase);

    public static int BigSwingDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BigSwingDamageDeadly, BigSwingDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var swing1 = new MoveStateDef(Swing1MoveId, SwingMove, new SingleAttackIntent(SwingDamage(cs)), Intents.Defend);
        var swing2 = new MoveStateDef(Swing2MoveId, SwingMove, new SingleAttackIntent(SwingDamage(cs)), Intents.Defend);
        var bigSwing = new MoveStateDef(BigSwingMoveId, BigSwingMove, new SingleAttackIntent(BigSwingDamage(cs)));
        swing1.FollowUpState = swing2;
        swing2.FollowUpState = bigSwing;
        bigSwing.FollowUpState = swing1;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { swing1, swing2, bigSwing }, swing1);
    }

    private static void BigSwingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BigSwingDamage(cs)).FromMonster(self).Execute();
    }

    private static void SwingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwingDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, SwingBlock(cs), ValueProp.Move, null);
    }
}
