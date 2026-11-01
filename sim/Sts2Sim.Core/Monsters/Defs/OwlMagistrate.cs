namespace Sts2Sim.Core.Monsters;

public sealed class OwlMagistrate : MonsterDef
{
    public const string MonsterIdEntry = "OWL_MAGISTRATE";
    public const string MagistrateScrutinyMoveId = "MAGISTRATE_SCRUTINY";
    public const string PeckAssaultMoveId = "PECK_ASSAULT";
    public const string JudicialFlightMoveId = "JUDICIAL_FLIGHT";
    public const string VerdictMoveId = "VERDICT";

    public const int InitialHpBase = 231;
    public const int InitialHpTough = 247;
    public const int VerdictDamageBase = 33;
    public const int VerdictDamageDeadly = 36;
    public const int ScrutinyDamageBase = 16;
    public const int ScrutinyDamageDeadly = 17;
    public const int PeckAssaultDamage = 4;
    public const int PeckAssaultRepeat = 6;
    public const int SoarAmount = 1;
    public const int VerdictVulnerable = 4;

    public OwlMagistrate()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int VerdictDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, VerdictDamageDeadly, VerdictDamageBase);

    public static int ScrutinyDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ScrutinyDamageDeadly, ScrutinyDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var scrutiny = new MoveStateDef(MagistrateScrutinyMoveId, MagistrateScrutinyMove, new SingleAttackIntent(ScrutinyDamage(cs)));
        var peckAssault = new MoveStateDef(PeckAssaultMoveId, PeckAssaultMove, new MultiAttackIntent(PeckAssaultDamage, PeckAssaultRepeat));
        var judicialFlight = new MoveStateDef(JudicialFlightMoveId, JudicialFlightMove, Intents.Buff);
        var verdict = new MoveStateDef(VerdictMoveId, VerdictMove, new SingleAttackIntent(VerdictDamage(cs)), new DebuffIntent());
        scrutiny.FollowUpState = peckAssault;
        peckAssault.FollowUpState = judicialFlight;
        judicialFlight.FollowUpState = verdict;
        verdict.FollowUpState = scrutiny;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { scrutiny, peckAssault, judicialFlight, verdict }, scrutiny);
    }

    private static void MagistrateScrutinyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ScrutinyDamage(cs)).FromMonster(self).Execute();
    }

    private static void PeckAssaultMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PeckAssaultDamage).WithHitCount(PeckAssaultRepeat).FromMonster(self).Execute();
    }

    private static void JudicialFlightMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<SoarPower>(cs, self, SoarAmount, self, null);
    }

    private static void VerdictMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, VerdictDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, targets, VerdictVulnerable, self, null);
        PowerCmd.Remove<SoarPower>(cs, self);
    }
}
