namespace Sts2Sim.Core.Monsters;

public sealed class Rocket : MonsterDef
{
    public const string MonsterIdEntry = "ROCKET";
    public const string TargetingReticleMoveId = "TARGETING_RETICLE_MOVE";
    public const string PrecisionBeamMoveId = "PRECISION_BEAM_MOVE";
    public const string ChargeUpMoveId = "CHARGE_UP_MOVE";
    public const string LaserMoveId = "LASER_MOVE";
    public const string RechargeMoveId = "RECHARGE_MOVE";

    public const int InitialHpBase = 199;
    public const int InitialHpTough = 209;
    public const int TargetingReticleDamageBase = 3;
    public const int TargetingReticleDamageDeadly = 4;
    public const int PrecisionBeamDamageBase = 18;
    public const int PrecisionBeamDamageDeadly = 20;
    public const int LaserDamageBase = 31;
    public const int LaserDamageDeadly = 35;
    public const int ChargeUpStrengthBase = 2;
    public const int ChargeUpStrengthDeadly = 3;

    public Rocket()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int TargetingReticleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TargetingReticleDamageDeadly, TargetingReticleDamageBase);

    public static int PrecisionBeamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PrecisionBeamDamageDeadly, PrecisionBeamDamageBase);

    public static int LaserDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LaserDamageDeadly, LaserDamageBase);

    public static int ChargeUpStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChargeUpStrengthDeadly, ChargeUpStrengthBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SurroundedPower>(cs, cs.GetOpponentsOf(self), 1m, self, null);
        PowerCmd.Apply<BackAttackRightPower>(cs, self, 1m, self, null);
        PowerCmd.Apply<CrabRagePower>(cs, self, 1m, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var targetingReticle = new MoveStateDef(TargetingReticleMoveId, TargetingReticleMove, new SingleAttackIntent(TargetingReticleDamage(cs)));
        var precisionBeam = new MoveStateDef(PrecisionBeamMoveId, PrecisionBeamMove, new SingleAttackIntent(PrecisionBeamDamage(cs)));
        var chargeUp = new MoveStateDef(ChargeUpMoveId, ChargeUpMove, Intents.Buff);
        var laser = new MoveStateDef(LaserMoveId, LaserMove, new SingleAttackIntent(LaserDamage(cs)));
        var recharge = new MoveStateDef(RechargeMoveId, RechargeMove, Intents.Sleep);
        targetingReticle.FollowUpState = precisionBeam;
        precisionBeam.FollowUpState = chargeUp;
        chargeUp.FollowUpState = laser;
        laser.FollowUpState = recharge;
        recharge.FollowUpState = targetingReticle;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { targetingReticle, precisionBeam, chargeUp, laser, recharge }, targetingReticle);
    }

    private static void TargetingReticleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TargetingReticleDamage(cs)).FromMonster(self).Execute();
    }

    private static void PrecisionBeamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PrecisionBeamDamage(cs)).FromMonster(self).Execute();
    }

    private static void ChargeUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, ChargeUpStrengthGain(cs), self, null);
    }

    private static void LaserMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LaserDamage(cs)).FromMonster(self).Execute();
    }

    private static void RechargeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
