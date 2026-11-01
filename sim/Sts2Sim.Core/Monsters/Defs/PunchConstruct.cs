namespace Sts2Sim.Core.Monsters;

public sealed class PunchConstructExtraState : MonsterExtraState
{
    public bool StartsWithFastPunch { get; set; }

    public int StartingHpReduction { get; set; }

    public override MonsterExtraState DeepClone() => new PunchConstructExtraState
    {
        StartsWithFastPunch = StartsWithFastPunch,
        StartingHpReduction = StartingHpReduction
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.PunchConstructExtra);
        w.WriteBool(StartsWithFastPunch);
        w.WriteInt32(StartingHpReduction);
    }
}

public sealed class PunchConstruct : MonsterDef
{
    public const string MonsterIdEntry = "PUNCH_CONSTRUCT";
    public const string ReadyMoveId = "READY_MOVE";
    public const string StrongPunchMoveId = "STRONG_PUNCH_MOVE";
    public const string FastPunchMoveId = "FAST_PUNCH_MOVE";

    public const int InitialHpBase = 55;
    public const int InitialHpTough = 60;
    public const int StrongPunchDamageBase = 14;
    public const int StrongPunchDamageDeadly = 16;
    public const int FastPunchDamageBase = 5;
    public const int FastPunchDamageDeadly = 6;
    public const int FastPunchRepeat = 2;
    public const int ReadyBlock = 10;
    public const int FastPunchFrail = 1;
    public const int ArtifactAmount = 1;

    public PunchConstruct()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int StrongPunchDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StrongPunchDamageDeadly, StrongPunchDamageBase);

    public static int FastPunchDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FastPunchDamageDeadly, FastPunchDamageBase);

    public static PunchConstructExtraState GetExtra(MonsterState monster)
        => monster.Extra as PunchConstructExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no PunchConstruct state");

    public override MonsterExtraState? CreateExtraState() => new PunchConstructExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<ArtifactPower>(cs, self, ArtifactAmount, self, null);
        int reduction = GetExtra(self).StartingHpReduction;
        if (reduction > 0)
        {
            CreatureInternals.SetCurrentHpInternal(self, Math.Max(1, self.CurrentHp - reduction));
        }
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var ready = new MoveStateDef(ReadyMoveId, ReadyMove, Intents.Defend);
        var strongPunch = new MoveStateDef(StrongPunchMoveId, StrongPunchMove, new SingleAttackIntent(StrongPunchDamage(cs)));
        var fastPunch = new MoveStateDef(FastPunchMoveId, FastPunchMove, new MultiAttackIntent(FastPunchDamage(cs), FastPunchRepeat), new DebuffIntent());
        ready.FollowUpState = fastPunch;
        fastPunch.FollowUpState = strongPunch;
        strongPunch.FollowUpState = ready;
        AiStateDef initial = GetExtra(self).StartsWithFastPunch ? fastPunch : ready;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { ready, fastPunch, strongPunch }, initial);
    }

    private static void FastPunchMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FastPunchDamage(cs)).WithHitCount(FastPunchRepeat).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, FastPunchFrail, self, null);
    }

    private static void ReadyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, ReadyBlock, ValueProp.Move, null);
    }

    private static void StrongPunchMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StrongPunchDamage(cs)).FromMonster(self).Execute();
    }
}
