namespace Sts2Sim.Core.Monsters;

public sealed class SpinyToad : MonsterDef
{
    public const string MonsterIdEntry = "SPINY_TOAD";
    public const string ProtrudingSpikesMoveId = "PROTRUDING_SPIKES_MOVE";
    public const string SpikeExplosionMoveId = "SPIKE_EXPLOSION_MOVE";
    public const string TongueLashMoveId = "TONGUE_LASH_MOVE";

    public const int MinInitialHpBase = 116;
    public const int MinInitialHpTough = 121;
    public const int MaxInitialHpBase = 119;
    public const int MaxInitialHpTough = 124;
    public const int LashDamageBase = 17;
    public const int LashDamageDeadly = 19;
    public const int ExplosionDamageBase = 23;
    public const int ExplosionDamageDeadly = 25;
    public const int SpikesThorns = 5;

    public SpinyToad()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int LashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LashDamageDeadly, LashDamageBase);

    public static int ExplosionDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ExplosionDamageDeadly, ExplosionDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var spikes = new MoveStateDef(ProtrudingSpikesMoveId, SpikesMove, Intents.Buff);
        var explosion = new MoveStateDef(SpikeExplosionMoveId, ExplosionMove, new SingleAttackIntent(ExplosionDamage(cs)));
        var lash = new MoveStateDef(TongueLashMoveId, LashMove, new SingleAttackIntent(LashDamage(cs)));
        spikes.FollowUpState = explosion;
        explosion.FollowUpState = lash;
        lash.FollowUpState = spikes;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { spikes, explosion, lash }, spikes);
    }

    private static void ExplosionMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ExplosionDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<ThornsPower>(cs, self, -SpikesThorns, self, null);
    }

    private static void LashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LashDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpikesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ThornsPower>(cs, self, SpikesThorns, self, null);
    }
}
