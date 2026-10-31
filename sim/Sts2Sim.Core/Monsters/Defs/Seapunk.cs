namespace Sts2Sim.Core.Monsters;

public sealed class Seapunk : MonsterDef
{
    public const string MonsterIdEntry = "SEAPUNK";
    public const string SeaKickMoveId = "SEA_KICK_MOVE";
    public const string SpinningKickMoveId = "SPINNING_KICK_MOVE";
    public const string BubbleBurpMoveId = "BUBBLE_BURP_MOVE";

    public const int MinInitialHpBase = 44;
    public const int MinInitialHpTough = 47;
    public const int MaxInitialHpBase = 46;
    public const int MaxInitialHpTough = 49;
    public const int SeaKickDamageBase = 11;
    public const int SeaKickDamageDeadly = 13;
    public const int SpinningKickDamage = 2;
    public const int SpinningKickRepeat = 4;
    public const int BubbleBlockBase = 7;
    public const int BubbleBlockTough = 8;
    public const int BubbleStrengthBase = 1;
    public const int BubbleStrengthDeadly = 2;

    public Seapunk()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SeaKickDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SeaKickDamageDeadly, SeaKickDamageBase);

    public static int BubbleBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, BubbleBlockTough, BubbleBlockBase);

    public static int BubbleStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BubbleStrengthDeadly, BubbleStrengthBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var seaKick = new MoveStateDef(SeaKickMoveId, SeaKickMove, new SingleAttackIntent(SeaKickDamage(cs)));
        var spinningKick = new MoveStateDef(SpinningKickMoveId, SpinningKickMove, new MultiAttackIntent(SpinningKickDamage, SpinningKickRepeat));
        var bubbleBurp = new MoveStateDef(BubbleBurpMoveId, BubbleBurpMove, Intents.Buff, Intents.Defend);
        seaKick.FollowUpState = spinningKick;
        spinningKick.FollowUpState = bubbleBurp;
        bubbleBurp.FollowUpState = seaKick;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { seaKick, spinningKick, bubbleBurp }, seaKick);
    }

    private static void SeaKickMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SeaKickDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpinningKickMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SpinningKickDamage).WithHitCount(SpinningKickRepeat).FromMonster(self).Execute();
    }

    private static void BubbleBurpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, BubbleBlock(cs), ValueProp.Move, null);
        PowerCmd.Apply<StrengthPower>(cs, self, BubbleStrength(cs), self, null);
    }
}
