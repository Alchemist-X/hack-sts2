namespace Sts2Sim.Core.Monsters;

public sealed class TorchHeadAmalgam : MonsterDef
{
    public const string MonsterIdEntry = "TORCH_HEAD_AMALGAM";
    public const string StrongTackleMoveId = "STRONG_TACKLE_MOVE";
    public const string Tackle2MoveId = "TACKLE_2_MOVE";
    public const string BeamMoveId = "BEAM_MOVE";
    public const string Tackle3MoveId = "TACKLE_3_MOVE";
    public const string Tackle4MoveId = "TACKLE_4_MOVE";

    public const int InitialHpBase = 199;
    public const int InitialHpTough = 211;
    public const int StrongTackleDamageBase = 26;
    public const int StrongTackleDamageDeadly = 32;
    public const int TackleDamageBase = 18;
    public const int TackleDamageDeadly = 22;
    public const int WeakTackleDamageBase = 14;
    public const int WeakTackleDamageDeadly = 16;
    public const int SoulBeamDamageBase = 8;
    public const int SoulBeamDamageDeadly = 8;
    public const int SoulBeamRepeat = 3;

    public TorchHeadAmalgam()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int StrongTackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StrongTackleDamageDeadly, StrongTackleDamageBase);

    public static int TackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TackleDamageDeadly, TackleDamageBase);

    public static int WeakTackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WeakTackleDamageDeadly, WeakTackleDamageBase);

    public static int SoulBeamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SoulBeamDamageDeadly, SoulBeamDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<MinionPower>(cs, self, 1m, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var strongTackle = new MoveStateDef(StrongTackleMoveId, StrongTackleMove, new SingleAttackIntent(StrongTackleDamage(cs)));
        var tackle2 = new MoveStateDef(Tackle2MoveId, TackleMove, new SingleAttackIntent(TackleDamage(cs)));
        var beam = new MoveStateDef(BeamMoveId, SoulBeamMove, new MultiAttackIntent(SoulBeamDamage(cs), SoulBeamRepeat));
        var tackle3 = new MoveStateDef(Tackle3MoveId, WeakTackleMove, new SingleAttackIntent(WeakTackleDamage(cs)));
        var tackle4 = new MoveStateDef(Tackle4MoveId, WeakTackleMove, new SingleAttackIntent(WeakTackleDamage(cs)));
        strongTackle.FollowUpState = tackle2;
        tackle2.FollowUpState = beam;
        beam.FollowUpState = tackle3;
        tackle3.FollowUpState = tackle4;
        tackle4.FollowUpState = beam;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { strongTackle, tackle2, beam, tackle3, tackle4 }, strongTackle);
    }

    private static void StrongTackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StrongTackleDamage(cs)).FromMonster(self).Execute();
    }

    private static void TackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TackleDamage(cs)).FromMonster(self).Execute();
    }

    private static void WeakTackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WeakTackleDamage(cs)).FromMonster(self).Execute();
    }

    private static void SoulBeamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SoulBeamDamage(cs)).WithHitCount(SoulBeamRepeat).FromMonster(self).Execute();
    }
}
