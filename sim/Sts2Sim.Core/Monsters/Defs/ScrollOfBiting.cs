namespace Sts2Sim.Core.Monsters;

public sealed class ScrollOfBitingExtraState : MonsterExtraState
{
    public int StarterMoveIdx { get; set; }

    public override MonsterExtraState DeepClone() => new ScrollOfBitingExtraState
    {
        StarterMoveIdx = StarterMoveIdx
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.ScrollOfBitingExtra);
        w.WriteInt32(StarterMoveIdx);
    }
}

public sealed class ScrollOfBiting : MonsterDef
{
    public const string MonsterIdEntry = "SCROLL_OF_BITING";
    public const string ChompMoveId = "CHOMP";
    public const string ChewMoveId = "CHEW";
    public const string MoreTeethMoveId = "MORE_TEETH";
    public const string RandStateId = "rand";

    public const int MinInitialHpBase = 30;
    public const int MinInitialHpTough = 33;
    public const int MaxInitialHpBase = 37;
    public const int MaxInitialHpTough = 39;
    public const int ChompDamageBase = 14;
    public const int ChompDamageDeadly = 16;
    public const int ChewDamageBase = 5;
    public const int ChewDamageDeadly = 6;
    public const int ChewRepeat = 2;
    public const int BuffAmt = 2;
    public const int PaperCutsAmount = 2;
    public const int StarterMoveCount = 3;

    public ScrollOfBiting()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ChompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChompDamageDeadly, ChompDamageBase);

    public static int ChewDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChewDamageDeadly, ChewDamageBase);

    public static ScrollOfBitingExtraState GetExtra(MonsterState monster)
        => monster.Extra as ScrollOfBitingExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no ScrollOfBiting state");

    public override MonsterExtraState? CreateExtraState() => new ScrollOfBitingExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<PaperCutsPower>(cs, self, PaperCutsAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var chomp = new MoveStateDef(ChompMoveId, ChompMove, new SingleAttackIntent(ChompDamage(cs)));
        var chew = new MoveStateDef(ChewMoveId, ChewState, new MultiAttackIntent(ChewDamage(cs), ChewRepeat));
        var moreTeeth = new MoveStateDef(MoreTeethMoveId, MoreTeethMove, Intents.Buff);
        var rand = new RandomBranchStateDef(RandStateId);
        chomp.FollowUpState = moreTeeth;
        chew.FollowUpState = rand;
        moreTeeth.FollowUpState = chew;
        rand.AddBranch(chomp, MoveRepeatType.CannotRepeat);
        rand.AddBranch(chew, 2);
        AiStateDef initial = (GetExtra(self).StarterMoveIdx % StarterMoveCount) switch
        {
            0 => chomp,
            1 => chew,
            _ => moreTeeth
        };
        return new MonsterMoveStateMachineDef(new AiStateDef[] { chomp, chew, moreTeeth, rand }, initial);
    }

    private static void ChewState(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ChewDamage(cs)).WithHitCount(ChewRepeat).FromMonster(self).Execute();
    }

    private static void ChompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ChompDamage(cs)).FromMonster(self).Execute();
    }

    private static void MoreTeethMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, BuffAmt, self, null);
    }
}
