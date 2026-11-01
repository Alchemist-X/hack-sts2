namespace Sts2Sim.Core.Monsters;

public sealed class BygoneEffigy : MonsterDef
{
    public const string MonsterIdEntry = "BYGONE_EFFIGY";
    public const string SleepMoveId = "SLEEP_MOVE";
    public const string WakeMoveId = "WAKE_MOVE";
    public const string Sleep2MoveId = "SLEEP_MOVE_2";
    public const string SlashesMoveId = "SLASHES_MOVE";

    public const int InitialHpBase = 127;
    public const int InitialHpTough = 132;
    public const int SlashDamageBase = 13;
    public const int SlashDamageDeadly = 15;
    public const int SlowAmount = 1;
    public const int WakeStrength = 10;

    public BygoneEffigy()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SlashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SlashDamageDeadly, SlashDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SlowPower>(cs, self, SlowAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var sleep = new MoveStateDef(SleepMoveId, InitialSleepMove, Intents.Sleep);
        var wake = new MoveStateDef(WakeMoveId, WakeMove, Intents.Buff);
        var sleep2 = new MoveStateDef(Sleep2MoveId, SleepMove, Intents.Sleep);
        var slashes = new MoveStateDef(SlashesMoveId, SlashMove, new SingleAttackIntent(SlashDamage(cs)));
        sleep.FollowUpState = wake;
        wake.FollowUpState = slashes;
        sleep2.FollowUpState = slashes;
        slashes.FollowUpState = slashes;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { sleep, wake, sleep2, slashes }, sleep);
    }

    private static void InitialSleepMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void SleepMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void WakeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, WakeStrength, self, null);
    }

    private static void SlashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SlashDamage(cs)).FromMonster(self).Execute();
    }
}
