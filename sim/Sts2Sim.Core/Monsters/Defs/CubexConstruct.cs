namespace Sts2Sim.Core.Monsters;

public sealed class CubexConstruct : MonsterDef
{
    public const string MonsterIdEntry = "CUBEX_CONSTRUCT";
    public const string ChargeUpMoveId = "CHARGE_UP_MOVE";
    public const string RepeaterBlastMoveId = "REPEATER_BLAST_MOVE";
    public const string RepeaterBlast2MoveId = "REPEATER_BLAST_MOVE_2";
    public const string ExpelMoveId = "EXPEL_MOVE";

    public const int InitialHpBase = 65;
    public const int InitialHpTough = 70;
    public const int BlastDamageBase = 7;
    public const int BlastDamageDeadly = 8;
    public const int ExpelDamageBase = 5;
    public const int ExpelDamageDeadly = 6;
    public const int ExpelRepeats = 2;
    public const int SpawnBlock = 13;
    public const int SpawnArtifact = 1;
    public const int StrengthGain = 2;

    public CubexConstruct()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int BlastDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BlastDamageDeadly, BlastDamageBase);

    public static int ExpelDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ExpelDamageDeadly, ExpelDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        CreatureCmd.GainBlock(cs, self, SpawnBlock, ValueProp.Move, null);
        PowerCmd.Apply<ArtifactPower>(cs, self, SpawnArtifact, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var chargeUp = new MoveStateDef(ChargeUpMoveId, ChargeUpMove, Intents.Buff);
        var blast = new MoveStateDef(RepeaterBlastMoveId, RepeaterBlastMove, new SingleAttackIntent(BlastDamage(cs)), Intents.Buff);
        var blast2 = new MoveStateDef(RepeaterBlast2MoveId, RepeaterBlastMove, new SingleAttackIntent(BlastDamage(cs)), Intents.Buff);
        var expel = new MoveStateDef(ExpelMoveId, ExpelBlastMove, new MultiAttackIntent(ExpelDamage(cs), ExpelRepeats));
        chargeUp.FollowUpState = blast;
        blast.FollowUpState = blast2;
        blast2.FollowUpState = expel;
        expel.FollowUpState = blast;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { chargeUp, blast, blast2, expel }, chargeUp);
    }

    private static void ChargeUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, StrengthGain, self, null);
    }

    private static void RepeaterBlastMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BlastDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, StrengthGain, self, null);
    }

    private static void ExpelBlastMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ExpelDamage(cs)).WithHitCount(ExpelRepeats).FromMonster(self).Execute();
    }
}
