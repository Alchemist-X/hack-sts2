namespace Sts2Sim.Core.Monsters;

public sealed class TurretOperator : MonsterDef
{
    public const string MonsterIdEntry = "TURRET_OPERATOR";
    public const string UnloadMoveId = "UNLOAD_MOVE";
    public const string Unload2MoveId = "UNLOAD_MOVE_2";
    public const string ReloadMoveId = "RELOAD_MOVE";

    public const int InitialHpBase = 41;
    public const int InitialHpTough = 51;
    public const int FireDamageBase = 3;
    public const int FireDamageDeadly = 4;
    public const int FireRepeat = 5;
    public const int ReloadStrength = 1;

    public TurretOperator()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int FireDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FireDamageDeadly, FireDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var unload = new MoveStateDef(UnloadMoveId, UnloadMove, new MultiAttackIntent(FireDamage(cs), FireRepeat));
        var unload2 = new MoveStateDef(Unload2MoveId, UnloadMove, new MultiAttackIntent(FireDamage(cs), FireRepeat));
        var reload = new MoveStateDef(ReloadMoveId, ReloadMove, Intents.Buff);
        unload.FollowUpState = unload2;
        unload2.FollowUpState = reload;
        reload.FollowUpState = unload;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { unload, unload2, reload }, unload);
    }

    private static void ReloadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, ReloadStrength, self, null);
    }

    private static void UnloadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FireDamage(cs)).WithHitCount(FireRepeat).FromMonster(self).Execute();
    }
}
