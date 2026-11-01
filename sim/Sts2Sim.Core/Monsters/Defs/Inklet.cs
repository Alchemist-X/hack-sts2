namespace Sts2Sim.Core.Monsters;

public sealed class InkletExtraState : MonsterExtraState
{
    public bool MiddleInklet { get; set; }

    public override MonsterExtraState DeepClone() => new InkletExtraState
    {
        MiddleInklet = MiddleInklet
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.InkletExtra);
        w.WriteBool(MiddleInklet);
    }
}

public sealed class Inklet : MonsterDef
{
    public const string MonsterIdEntry = "INKLET";
    public const string JabMoveId = "JAB_MOVE";
    public const string WhirlwindMoveId = "WHIRLWIND_MOVE";
    public const string PiercingGazeMoveId = "PIERCING_GAZE_MOVE";
    public const string InitRandStateId = "INIT_RAND";
    public const string RandStateId = "RAND";

    public const int MinInitialHpBase = 11;
    public const int MinInitialHpTough = 12;
    public const int MaxInitialHpBase = 17;
    public const int MaxInitialHpTough = 18;
    public const int JabDamageBase = 3;
    public const int JabDamageDeadly = 4;
    public const int WhirlwindDamageBase = 2;
    public const int WhirlwindDamageDeadly = 3;
    public const int WhirlwindRepeat = 3;
    public const int PiercingGazeDamageBase = 10;
    public const int PiercingGazeDamageDeadly = 11;
    public const int SlipperyAmount = 1;

    public Inklet()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int JabDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, JabDamageDeadly, JabDamageBase);

    public static int WhirlwindDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WhirlwindDamageDeadly, WhirlwindDamageBase);

    public static int PiercingGazeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PiercingGazeDamageDeadly, PiercingGazeDamageBase);

    public static InkletExtraState GetExtra(MonsterState monster)
        => monster.Extra as InkletExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Inklet state");

    public override MonsterExtraState? CreateExtraState() => new InkletExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SlipperyPower>(cs, self, SlipperyAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var jab = new MoveStateDef(JabMoveId, JabMove, new SingleAttackIntent(JabDamage(cs)));
        var whirlwind = new MoveStateDef(WhirlwindMoveId, WhirlwindMove, new MultiAttackIntent(WhirlwindDamage(cs), WhirlwindRepeat));
        var piercingGaze = new MoveStateDef(PiercingGazeMoveId, PiercingGazeMove, new SingleAttackIntent(PiercingGazeDamage(cs)));
        var initRand = new RandomBranchStateDef(InitRandStateId);
        var rand = new RandomBranchStateDef(RandStateId);
        initRand.AddBranch(jab, 2, 1f);
        initRand.AddBranch(whirlwind, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(piercingGaze, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(whirlwind, MoveRepeatType.CannotRepeat, 1f);
        jab.FollowUpState = rand;
        whirlwind.FollowUpState = jab;
        piercingGaze.FollowUpState = jab;
        AiStateDef initial = GetExtra(self).MiddleInklet ? whirlwind : jab;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { jab, piercingGaze, whirlwind, rand }, initial);
    }

    private static void JabMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, JabDamage(cs)).FromMonster(self).Execute();
    }

    private static void PiercingGazeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PiercingGazeDamage(cs)).FromMonster(self).Execute();
    }

    private static void WhirlwindMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WhirlwindDamage(cs)).WithHitCount(WhirlwindRepeat).FromMonster(self).Execute();
    }
}
