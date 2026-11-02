namespace Sts2Sim.Core.Monsters;

public sealed class CalcifiedCultist : MonsterDef
{
    public const string MonsterIdEntry = "CALCIFIED_CULTIST";
    public const string IncantationMoveId = "INCANTATION_MOVE";
    public const string DarkStrikeMoveId = "DARK_STRIKE_MOVE";

    public const int MinInitialHpBase = 38;
    public const int MinInitialHpTough = 39;
    public const int MaxInitialHpBase = 41;
    public const int MaxInitialHpTough = 42;
    public const int DarkStrikeDamageBase = 9;
    public const int DarkStrikeDamageDeadly = 11;
    public const int IncantationAmount = 2;

    public CalcifiedCultist()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int DarkStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DarkStrikeDamageDeadly, DarkStrikeDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var incantation = new MoveStateDef(IncantationMoveId, IncantationMove, Intents.Buff);
        var darkStrike = new MoveStateDef(DarkStrikeMoveId, DarkStrikeMove, new SingleAttackIntent(DarkStrikeDamage(cs)));
        incantation.FollowUpState = darkStrike;
        darkStrike.FollowUpState = darkStrike;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { incantation, darkStrike }, incantation);
    }

    private static void IncantationMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<RitualPower>(cs, self, IncantationAmount, self, null);
    }

    private static void DarkStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DarkStrikeDamage(cs)).FromMonster(self).Execute();
    }
}
