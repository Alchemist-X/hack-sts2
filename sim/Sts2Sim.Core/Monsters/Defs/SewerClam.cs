namespace Sts2Sim.Core.Monsters;

public sealed class SewerClam : MonsterDef
{
    public const string MonsterIdEntry = "SEWER_CLAM";
    public const string PressurizeMoveId = "PRESSURIZE_MOVE";
    public const string JetMoveId = "JET_MOVE";

    public const int InitialHpBase = 56;
    public const int InitialHpTough = 58;
    public const int JetDamageBase = 10;
    public const int JetDamageDeadly = 11;
    public const int PlatingBase = 8;
    public const int PlatingTough = 9;
    public const int PressurizeStrength = 4;

    public SewerClam()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int JetDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, JetDamageDeadly, JetDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        int plating = cs.GetValueIfAscension(AscensionLevel.ToughEnemies, PlatingTough, PlatingBase);
        PowerCmd.Apply<PlatingPower>(cs, self, plating, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var pressurize = new MoveStateDef(PressurizeMoveId, PressurizeMove, Intents.Buff);
        var jet = new MoveStateDef(JetMoveId, JetMove, new SingleAttackIntent(JetDamage(cs)));
        pressurize.FollowUpState = jet;
        jet.FollowUpState = pressurize;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { pressurize, jet }, jet);
    }

    private static void JetMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, JetDamage(cs)).FromMonster(self).Execute();
    }

    private static void PressurizeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, PressurizeStrength, self, null);
    }
}
