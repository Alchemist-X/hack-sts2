namespace Sts2Sim.Core.Monsters;

public sealed class BruteRubyRaider : MonsterDef
{
    public const string MonsterIdEntry = "BRUTE_RUBY_RAIDER";
    public const string BeatMoveId = "BEAT_MOVE";
    public const string RoarMoveId = "ROAR_MOVE";

    public const int MinInitialHpBase = 30;
    public const int MinInitialHpTough = 31;
    public const int MaxInitialHpBase = 33;
    public const int MaxInitialHpTough = 34;
    public const int BeatDamageBase = 7;
    public const int BeatDamageDeadly = 8;
    public const int RoarStrength = 3;

    public BruteRubyRaider()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int BeatDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BeatDamageDeadly, BeatDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var beat = new MoveStateDef(BeatMoveId, BeatMove, new SingleAttackIntent(BeatDamage(cs)));
        var roar = new MoveStateDef(RoarMoveId, RoarMove, Intents.Buff);
        beat.FollowUpState = roar;
        roar.FollowUpState = beat;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { beat, roar }, beat);
    }

    private static void BeatMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BeatDamage(cs)).FromMonster(self).Execute();
    }

    private static void RoarMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, RoarStrength, self, null);
    }
}
