namespace Sts2Sim.Core.Monsters;

public sealed class KinPriest : MonsterDef
{
    public const string MonsterIdEntry = "KIN_PRIEST";
    public const string OrbOfFrailtyMoveId = "ORB_OF_FRAILTY_MOVE";
    public const string OrbOfWeaknessMoveId = "ORB_OF_WEAKNESS_MOVE";
    public const string BeamMoveId = "BEAM_MOVE";
    public const string RitualMoveId = "RITUAL_MOVE";

    public const int InitialHpBase = 190;
    public const int InitialHpTough = 199;
    public const int OrbDamageBase = 8;
    public const int OrbDamageDeadly = 9;
    public const int BeamDamage = 3;
    public const int BeamRepeat = 3;
    public const int RitualStrengthBase = 2;
    public const int RitualStrengthDeadly = 3;
    public const int OrbDebuffAmount = 1;

    public KinPriest()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int OrbOfFrailtyDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, OrbDamageDeadly, OrbDamageBase);

    public static int OrbOfWeaknessDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, OrbDamageDeadly, OrbDamageBase);

    public static int RitualStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RitualStrengthDeadly, RitualStrengthBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var orbOfFrailty = new MoveStateDef(OrbOfFrailtyMoveId, OrbOfFrailtyMove, new SingleAttackIntent(OrbOfFrailtyDamage(cs)), new DebuffIntent());
        var orbOfWeakness = new MoveStateDef(OrbOfWeaknessMoveId, OrbOfWeaknessMove, new SingleAttackIntent(OrbOfWeaknessDamage(cs)), new DebuffIntent());
        var beam = new MoveStateDef(BeamMoveId, BeamMove, new MultiAttackIntent(BeamDamage, BeamRepeat));
        var ritual = new MoveStateDef(RitualMoveId, RitualMove, Intents.Buff);
        orbOfFrailty.FollowUpState = orbOfWeakness;
        orbOfWeakness.FollowUpState = beam;
        beam.FollowUpState = ritual;
        ritual.FollowUpState = orbOfFrailty;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { orbOfFrailty, orbOfWeakness, beam, ritual }, orbOfFrailty);
    }

    private static void BeamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BeamDamage).WithHitCount(BeamRepeat).FromMonster(self).Execute();
    }

    private static void OrbOfFrailtyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, OrbOfFrailtyDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, OrbDebuffAmount, self, null);
    }

    private static void OrbOfWeaknessMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, OrbOfWeaknessDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, OrbDebuffAmount, self, null);
    }

    private static void RitualMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, RitualStrength(cs), self, null);
    }
}
