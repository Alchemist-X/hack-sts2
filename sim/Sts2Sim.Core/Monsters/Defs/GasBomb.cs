namespace Sts2Sim.Core.Monsters;

public sealed class GasBomb : MonsterDef
{
    public const string MonsterIdEntry = "GAS_BOMB";
    public const string ExplodeMoveId = "EXPLODE_MOVE";

    public const int InitialHpBase = 7;
    public const int InitialHpTough = 8;
    public const int ExplodeDamageBase = 8;
    public const int ExplodeDamageDeadly = 9;

    public GasBomb()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ExplodeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ExplodeDamageDeadly, ExplodeDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<MinionPower>(cs, self, 1m, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var explode = new MoveStateDef(ExplodeMoveId, ExplodeMove, new DeathBlowIntent(static (c, _) => ExplodeDamage(c)));
        return new MonsterMoveStateMachineDef(new AiStateDef[] { explode }, explode);
    }

    private static void ExplodeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ExplodeDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.Kill(cs, self);
    }
}
