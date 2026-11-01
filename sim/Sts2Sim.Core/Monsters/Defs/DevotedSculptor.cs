namespace Sts2Sim.Core.Monsters;

public sealed class DevotedSculptor : MonsterDef
{
    public const string MonsterIdEntry = "DEVOTED_SCULPTOR";
    public const string ForbiddenIncantationMoveId = "FORBIDDEN_INCANTATION_MOVE";
    public const string SavageMoveId = "SAVAGE_MOVE";

    public const int InitialHpBase = 162;
    public const int InitialHpTough = 172;
    public const int SavageDamageBase = 12;
    public const int SavageDamageDeadly = 15;
    public const int RitualGain = 9;

    public DevotedSculptor()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SavageDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SavageDamageDeadly, SavageDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var incantation = new MoveStateDef(ForbiddenIncantationMoveId, ForbiddenIncantationMove, Intents.Buff);
        var savage = new MoveStateDef(SavageMoveId, SavageMove, new SingleAttackIntent(SavageDamage(cs)));
        incantation.FollowUpState = savage;
        savage.FollowUpState = savage;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { incantation, savage }, incantation);
    }

    private static void ForbiddenIncantationMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<RitualPower>(cs, self, RitualGain, null, null);
    }

    private static void SavageMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SavageDamage(cs)).FromMonster(self).Execute();
    }
}
