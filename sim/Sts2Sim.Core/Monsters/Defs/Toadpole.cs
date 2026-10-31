namespace Sts2Sim.Core.Monsters;

public sealed class ToadpoleExtraState : MonsterExtraState
{
    public const ushort CanonicalTag = 0x4D31;

    public bool IsFront { get; set; }

    public override MonsterExtraState DeepClone() => new ToadpoleExtraState
    {
        IsFront = IsFront
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CanonicalTag);
        w.WriteBool(IsFront);
    }
}

public sealed class Toadpole : MonsterDef
{
    public const string MonsterIdEntry = "TOADPOLE";
    public const string InitMoveId = "INIT_MOVE";
    public const string SpikeSpitMoveId = "SPIKE_SPIT_MOVE";
    public const string WhirlMoveId = "WHIRL_MOVE";
    public const string SpikenMoveId = "SPIKEN_MOVE";

    public const int MinInitialHpBase = 21;
    public const int MinInitialHpTough = 22;
    public const int MaxInitialHpBase = 25;
    public const int MaxInitialHpTough = 26;
    public const int SpikeSpitDamageBase = 3;
    public const int SpikeSpitDamageDeadly = 4;
    public const int SpikeSpitRepeat = 3;
    public const int WhirlDamageBase = 7;
    public const int WhirlDamageDeadly = 8;
    public const int SpikenAmount = 2;

    public Toadpole()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SpikeSpitDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SpikeSpitDamageDeadly, SpikeSpitDamageBase);

    public static int WhirlDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WhirlDamageDeadly, WhirlDamageBase);

    public static ToadpoleExtraState GetExtra(MonsterState monster)
        => monster.Extra as ToadpoleExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Toadpole state");

    public override MonsterExtraState? CreateExtraState() => new ToadpoleExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var spikeSpit = new MoveStateDef(SpikeSpitMoveId, SpikeSpitMove, new MultiAttackIntent(SpikeSpitDamage(cs), SpikeSpitRepeat));
        var whirl = new MoveStateDef(WhirlMoveId, WhirlMove, new SingleAttackIntent(WhirlDamage(cs)));
        var spiken = new MoveStateDef(SpikenMoveId, SpikenMove, Intents.Buff);
        var init = new ConditionalBranchStateDef(InitMoveId);
        whirl.FollowUpState = spiken;
        spiken.FollowUpState = spikeSpit;
        spikeSpit.FollowUpState = whirl;
        init.AddState(whirl, static (_, m) => !GetExtra(m).IsFront);
        init.AddState(spiken, static (_, m) => GetExtra(m).IsFront);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { init, spiken, spikeSpit, whirl }, init);
    }

    private static void SpikeSpitMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ThornsPower>(cs, self, -SpikenAmount, self, null);
        DamageCmd.Attack(cs, SpikeSpitDamage(cs)).WithHitCount(SpikeSpitRepeat).FromMonster(self).Execute();
    }

    private static void WhirlMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WhirlDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpikenMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ThornsPower>(cs, self, SpikenAmount, self, null);
    }
}
