namespace Sts2Sim.Core.Monsters;

public sealed class TerrorEel : MonsterDef
{
    public const string MonsterIdEntry = "TERROR_EEL";
    public const string CrashMoveId = "CRASH_MOVE";
    public const string ThrashMoveId = "THRASH_MOVE";
    public const string StunMoveId = "STUN_MOVE";
    public const string TerrorMoveId = "TERROR_MOVE";

    public const int HpNormal = 140;
    public const int HpTough = 150;
    public const int ShriekAmountBase = 70;
    public const int ShriekAmountTough = 75;
    public const int CrashDamageBase = 16;
    public const int CrashDamageDeadly = 18;
    public const int ThrashDamageBase = 3;
    public const int ThrashDamageDeadly = 4;
    public const int ThrashRepeat = 3;
    public const int ThrashVigor = 6;
    public const int TerrorVulnerable = 99;

    public TerrorEel()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpNormal);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ShriekAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, ShriekAmountTough, ShriekAmountBase);

    public static int CrashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, CrashDamageDeadly, CrashDamageBase);

    public static int ThrashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThrashDamageDeadly, ThrashDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<ShriekPower>(cs, self, ShriekAmount(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var crash = new MoveStateDef(CrashMoveId, CrashMove, new SingleAttackIntent(CrashDamage(cs)));
        var thrash = new MoveStateDef(ThrashMoveId, ThrashMove, new MultiAttackIntent(ThrashDamage(cs), ThrashRepeat), Intents.Buff);
        var stun = new MoveStateDef(StunMoveId, StunMove, Intents.Stun);
        var terror = new MoveStateDef(TerrorMoveId, TerrorMove, new DebuffIntent());
        crash.FollowUpState = thrash;
        thrash.FollowUpState = crash;
        stun.FollowUpState = terror;
        terror.FollowUpState = crash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { crash, thrash, stun, terror }, crash);
    }

    private static void CrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, CrashDamage(cs)).FromMonster(self).Execute();
    }

    private static void ThrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrashDamage(cs)).WithHitCount(ThrashRepeat).FromMonster(self).Execute();
        PowerCmd.Apply<VigorPower>(cs, self, ThrashVigor, self, null);
    }

    private static void StunMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void TerrorMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<VulnerablePower>(cs, targets, TerrorVulnerable, self, null);
    }
}
