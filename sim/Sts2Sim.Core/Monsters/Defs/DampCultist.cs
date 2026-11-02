namespace Sts2Sim.Core.Monsters;

public sealed class DampCultist : MonsterDef
{
    public const string MonsterIdEntry = "DAMP_CULTIST";
    public const string IncantationMoveId = "INCANTATION_MOVE";
    public const string DarkStrikeMoveId = "DARK_STRIKE_MOVE";

    public const int MinInitialHpBase = 51;
    public const int MinInitialHpTough = 52;
    public const int MaxInitialHpBase = 53;
    public const int MaxInitialHpTough = 54;
    public const int DarkStrikeDamageBase = 1;
    public const int DarkStrikeDamageDeadly = 3;
    public const int IncantationAmountBase = 5;
    public const int IncantationAmountDeadly = 6;

    public DampCultist()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int DarkStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DarkStrikeDamageDeadly, DarkStrikeDamageBase);

    public static int IncantationAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, IncantationAmountDeadly, IncantationAmountBase);

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
        PowerCmd.Apply<RitualPower>(cs, self, IncantationAmount(cs), self, null);
    }

    private static void DarkStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DarkStrikeDamage(cs)).FromMonster(self).Execute();
    }
}
