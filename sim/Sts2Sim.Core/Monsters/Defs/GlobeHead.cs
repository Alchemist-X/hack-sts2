namespace Sts2Sim.Core.Monsters;

public sealed class GlobeHead : MonsterDef
{
    public const string MonsterIdEntry = "GLOBE_HEAD";
    public const string ThunderStrikeMoveId = "THUNDER_STRIKE";
    public const string ShockingSlapMoveId = "SHOCKING_SLAP";
    public const string GalvanicBurstMoveId = "GALVANIC_BURST";

    public const int HpBase = 148;
    public const int HpTough = 158;
    public const int ThunderStrikeDamageBase = 6;
    public const int ThunderStrikeDamageDeadly = 7;
    public const int ThunderStrikeRepeat = 3;
    public const int ShockingSlapDamageBase = 13;
    public const int ShockingSlapDamageDeadly = 14;
    public const int ShockingSlapFrail = 2;
    public const int GalvanicBurstDamageBase = 16;
    public const int GalvanicBurstDamageDeadly = 17;
    public const int GalvanicBurstStr = 2;
    public const int GalvanicPowerAmountBase = 6;
    public const int GalvanicPowerAmountDeadly = 8;

    public GlobeHead()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ThunderStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThunderStrikeDamageDeadly, ThunderStrikeDamageBase);

    public static int ShockingSlapDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ShockingSlapDamageDeadly, ShockingSlapDamageBase);

    public static int GalvanicBurstDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GalvanicBurstDamageDeadly, GalvanicBurstDamageBase);

    public static int GalvanicPowerAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GalvanicPowerAmountDeadly, GalvanicPowerAmountBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<GalvanicPower>(cs, self, GalvanicPowerAmount(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var thunderStrike = new MoveStateDef(ThunderStrikeMoveId, ThunderStrike, new MultiAttackIntent(ThunderStrikeDamage(cs), ThunderStrikeRepeat));
        var shockingSlap = new MoveStateDef(ShockingSlapMoveId, ShockingSlap, new SingleAttackIntent(ShockingSlapDamage(cs)), new DebuffIntent());
        var galvanicBurst = new MoveStateDef(GalvanicBurstMoveId, GalvanicBurstMove, new SingleAttackIntent(GalvanicBurstDamage(cs)), Intents.Buff);
        shockingSlap.FollowUpState = thunderStrike;
        thunderStrike.FollowUpState = galvanicBurst;
        galvanicBurst.FollowUpState = shockingSlap;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { shockingSlap, thunderStrike, galvanicBurst }, shockingSlap);
    }

    private static void ThunderStrike(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThunderStrikeDamage(cs)).WithHitCount(ThunderStrikeRepeat).FromMonster(self).Execute();
    }

    private static void ShockingSlap(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ShockingSlapDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, ShockingSlapFrail, self, null);
    }

    private static void GalvanicBurstMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GalvanicBurstDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, GalvanicBurstStr, self, null);
    }
}
