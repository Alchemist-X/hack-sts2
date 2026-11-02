namespace Sts2Sim.Core.Monsters;

public sealed class InfestedPrism : MonsterDef
{
    public const string MonsterIdEntry = "INFESTED_PRISM";
    public const string JabMoveId = "JAB_MOVE";
    public const string RadiateMoveId = "RADIATE_MOVE";
    public const string WhirlwindMoveId = "WHIRLWIND_MOVE";
    public const string PulsateMoveId = "PULSATE_MOVE";

    public const int HpBase = 161;
    public const int HpTough = 171;
    public const int JabDamageBase = 15;
    public const int JabDamageDeadly = 17;
    public const int VitalSparkAmountBase = 2;
    public const int VitalSparkAmountDeadly = 3;
    public const int PulsateDamageBase = 8;
    public const int PulsateDamageDeadly = 10;
    public const int PulsateBlockBase = 20;
    public const int PulsateBlockTough = 22;
    public const int RadiateDamageBase = 11;
    public const int RadiateDamageDeadly = 13;
    public const int RadiateBlockBase = 11;
    public const int RadiateBlockDeadly = 13;
    public const int WhirlwindDamageBase = 5;
    public const int WhirlwindDamageDeadly = 6;
    public const int WhirlwindRepeat = 3;

    public InfestedPrism()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int JabDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, JabDamageDeadly, JabDamageBase);

    public static int VitalSparkAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, VitalSparkAmountDeadly, VitalSparkAmountBase);

    public static int PulsateDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PulsateDamageDeadly, PulsateDamageBase);

    public static int PulsateBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, PulsateBlockTough, PulsateBlockBase);

    public static int RadiateDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RadiateDamageDeadly, RadiateDamageBase);

    public static int RadiateBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RadiateBlockDeadly, RadiateBlockBase);

    public static int WhirlwindDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WhirlwindDamageDeadly, WhirlwindDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<VitalSparkPower>(cs, self, VitalSparkAmount(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var jab = new MoveStateDef(JabMoveId, JabMove, new SingleAttackIntent(JabDamage(cs)));
        var radiate = new MoveStateDef(RadiateMoveId, RadiateMove, new SingleAttackIntent(RadiateDamage(cs)), Intents.Defend);
        var whirlwind = new MoveStateDef(WhirlwindMoveId, WhirlwindMove, new MultiAttackIntent(WhirlwindDamage(cs), WhirlwindRepeat));
        var pulsate = new MoveStateDef(PulsateMoveId, PulsateMove, new SingleAttackIntent(PulsateDamage(cs)), Intents.Buff, Intents.Defend);
        jab.FollowUpState = radiate;
        radiate.FollowUpState = whirlwind;
        whirlwind.FollowUpState = pulsate;
        pulsate.FollowUpState = jab;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { jab, radiate, whirlwind, pulsate }, jab);
    }

    private static void JabMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, JabDamage(cs)).FromMonster(self).Execute();
    }

    private static void RadiateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RadiateDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, RadiateBlock(cs), ValueProp.Move, null);
    }

    private static void WhirlwindMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WhirlwindDamage(cs)).WithHitCount(WhirlwindRepeat).FromMonster(self).Execute();
    }

    private static void PulsateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PulsateDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, PulsateBlock(cs), ValueProp.Move, null);
        PowerCmd.Apply<VitalSparkPower>(cs, self, VitalSparkAmount(cs), self, null);
    }
}
