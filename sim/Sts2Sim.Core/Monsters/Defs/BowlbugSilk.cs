namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugSilk : MonsterDef
{
    public const string MonsterIdEntry = "BOWLBUG_SILK";
    public const string ThrashMoveId = "THRASH_MOVE";
    public const string ToxicSpitMoveId = "TOXIC_SPIT_MOVE";

    public const int MinInitialHpBase = 40;
    public const int MinInitialHpTough = 41;
    public const int MaxInitialHpBase = 43;
    public const int MaxInitialHpTough = 44;
    public const int ThrashDamageBase = 4;
    public const int ThrashDamageDeadly = 5;
    public const int ThrashRepeat = 2;
    public const int SpitWeakAmount = 1;

    public BowlbugSilk()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ThrashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThrashDamageDeadly, ThrashDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var thrash = new MoveStateDef(ThrashMoveId, ThrashMove, new MultiAttackIntent(ThrashDamage(cs), ThrashRepeat));
        var spit = new MoveStateDef(ToxicSpitMoveId, SpitMove, new DebuffIntent());
        thrash.FollowUpState = spit;
        spit.FollowUpState = thrash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { thrash, spit }, spit);
    }

    private static void SpitMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<WeakPower>(cs, targets, SpitWeakAmount, self, null);
    }

    private static void ThrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrashDamage(cs)).WithHitCount(ThrashRepeat).FromMonster(self).Execute();
    }
}
