namespace Sts2Sim.Core.Monsters;

public sealed class TheObscuraExtraState : MonsterExtraState
{
    public bool HasSummoned { get; set; }

    public override MonsterExtraState DeepClone() => new TheObscuraExtraState
    {
        HasSummoned = HasSummoned
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.TheObscuraExtra);
        w.WriteBool(HasSummoned);
    }
}

public sealed class TheObscura : MonsterDef
{
    public const string MonsterIdEntry = "THE_OBSCURA";
    public const string IllusionMoveId = "ILLUSION_MOVE";
    public const string PiercingGazeMoveId = "PIERCING_GAZE_MOVE";
    public const string SailMoveId = "SAIL_MOVE";
    public const string HardeningStrikeMoveId = "HARDENING_STRIKE_MOVE";
    public const string RandMoveId = "RAND";
    public const string IllusionSlot = "illusion";

    public const int HpBase = 123;
    public const int HpTough = 129;
    public const int PiercingGazeDamageBase = 10;
    public const int PiercingGazeDamageDeadly = 11;
    public const int HardeningStrikeDamageBase = 6;
    public const int HardeningStrikeDamageDeadly = 7;
    public const int HardeningStrikeBlockBase = 6;
    public const int HardeningStrikeBlockDeadly = 7;
    public const int WailStrength = 3;

    public TheObscura()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int PiercingGazeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PiercingGazeDamageDeadly, PiercingGazeDamageBase);

    public static int HardeningStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HardeningStrikeDamageDeadly, HardeningStrikeDamageBase);

    public static int HardeningStrikeBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HardeningStrikeBlockDeadly, HardeningStrikeBlockBase);

    public static TheObscuraExtraState GetExtra(MonsterState monster)
        => monster.Extra as TheObscuraExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no TheObscura state");

    public override MonsterExtraState? CreateExtraState() => new TheObscuraExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var illusion = new MoveStateDef(IllusionMoveId, IllusionMove, Intents.Summon);
        var piercingGaze = new MoveStateDef(PiercingGazeMoveId, PiercingGazeMove, new SingleAttackIntent(PiercingGazeDamage(cs)));
        var wail = new MoveStateDef(SailMoveId, WailMove, Intents.Buff);
        var hardeningStrike = new MoveStateDef(HardeningStrikeMoveId, HardeningStrikeMove, new SingleAttackIntent(HardeningStrikeDamage(cs)), Intents.Defend);
        var rand = new RandomBranchStateDef(RandMoveId);
        illusion.FollowUpState = rand;
        piercingGaze.FollowUpState = rand;
        wail.FollowUpState = rand;
        hardeningStrike.FollowUpState = rand;
        rand.AddBranch(piercingGaze, MoveRepeatType.CannotRepeat);
        rand.AddBranch(wail, MoveRepeatType.CannotRepeat);
        rand.AddBranch(hardeningStrike, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { illusion, piercingGaze, wail, hardeningStrike, rand }, illusion);
    }

    private static void IllusionMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        if (cs.IsInProgress)
        {
            CreatureCmd.Add(cs, MonsterRegistry.Get(Parafright.MonsterIdEntry), CombatSide.Enemy, IllusionSlot);
        }
        GetExtra(self).HasSummoned = true;
    }

    private static void PiercingGazeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PiercingGazeDamage(cs)).FromMonster(self).Execute();
    }

    private static void WailMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, cs.GetTeammatesOf(self), WailStrength, self, null);
    }

    private static void HardeningStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HardeningStrikeDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, HardeningStrikeBlock(cs), ValueProp.Move, null);
    }
}
