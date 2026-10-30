namespace Sts2Sim.Core.Monsters;

public sealed class NibbitExtraState : MonsterExtraState
{
    public bool IsFront { get; set; }

    public bool IsAlone { get; set; }

    public override MonsterExtraState DeepClone() => new NibbitExtraState
    {
        IsFront = IsFront,
        IsAlone = IsAlone
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.NibbitExtra);
        w.WriteBool(IsFront);
        w.WriteBool(IsAlone);
    }
}

public sealed class Nibbit : MonsterDef
{
    public const string MonsterIdEntry = "NIBBIT";
    public const string InitMoveId = "INIT_MOVE";
    public const string ButtMoveId = "BUTT_MOVE";
    public const string SliceMoveId = "SLICE_MOVE";
    public const string HissMoveId = "HISS_MOVE";

    public const int MinInitialHpBase = 42;
    public const int MinInitialHpTough = 44;
    public const int MaxInitialHpBase = 46;
    public const int MaxInitialHpTough = 48;
    public const int ButtDamageBase = 12;
    public const int ButtDamageDeadly = 13;
    public const int SliceBlockBase = 5;
    public const int SliceBlockTough = 6;
    public const int SliceDamageBase = 6;
    public const int SliceDamageDeadly = 7;
    public const int HissStrengthBase = 2;
    public const int HissStrengthDeadly = 3;

    public Nibbit()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ButtDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ButtDamageDeadly, ButtDamageBase);

    public static int SliceBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, SliceBlockTough, SliceBlockBase);

    public static int SliceDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SliceDamageDeadly, SliceDamageBase);

    public static int HissStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HissStrengthDeadly, HissStrengthBase);

    public static NibbitExtraState GetExtra(MonsterState monster)
        => monster.Extra as NibbitExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Nibbit state");

    public override MonsterExtraState? CreateExtraState() => new NibbitExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var butt = new MoveStateDef(ButtMoveId, ButtMove, new SingleAttackIntent(ButtDamage(cs)));
        var slice = new MoveStateDef(SliceMoveId, SliceMove, new SingleAttackIntent(SliceDamage(cs)), Intents.Defend);
        var hiss = new MoveStateDef(HissMoveId, HissMove, Intents.Buff);
        var init = new ConditionalBranchStateDef(InitMoveId);
        if (GetExtra(self).IsAlone)
        {
            init.AddState(butt, static (_, m) => GetExtra(m).IsAlone);
        }
        else
        {
            init.AddState(hiss, static (_, m) => !GetExtra(m).IsFront);
            init.AddState(slice, static (_, m) => GetExtra(m).IsFront);
        }
        slice.FollowUpState = hiss;
        butt.FollowUpState = slice;
        hiss.FollowUpState = butt;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { init, butt, slice, hiss }, init);
    }

    private static void ButtMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ButtDamage(cs)).FromMonster(self).Execute();
    }

    private static void SliceMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SliceDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, SliceBlock(cs), ValueProp.Move, null);
    }

    private static void HissMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, HissStrengthGain(cs), self, null);
    }
}
