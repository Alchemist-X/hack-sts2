namespace Sts2Sim.Core.Monsters;

public sealed class Vantom : MonsterDef
{
    public const string MonsterIdEntry = "VANTOM";
    public const string InkBlotMoveId = "INK_BLOT_MOVE";
    public const string InkyLanceMoveId = "INKY_LANCE_MOVE";
    public const string DismemberMoveId = "DISMEMBER_MOVE";
    public const string PrepareMoveId = "PREPARE_MOVE";

    public const string WoundCardId = "WOUND";

    public const int InitialHpBase = 173;
    public const int InitialHpTough = 183;
    public const int SlipperyAmtBase = 8;
    public const int SlipperyAmtTough = 9;
    public const int InkBlotDamageBase = 7;
    public const int InkBlotDamageDeadly = 8;
    public const int InkyLanceDamageBase = 6;
    public const int InkyLanceDamageDeadly = 7;
    public const int DismemberDamageBase = 26;
    public const int DismemberDamageDeadly = 30;
    public const int InkyLanceRepeat = 2;
    public const int DismemberWounds = 3;
    public const int PrepareStrength = 2;

    public Vantom()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SlipperyAmt(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, SlipperyAmtTough, SlipperyAmtBase);

    public static int InkBlotDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, InkBlotDamageDeadly, InkBlotDamageBase);

    public static int InkyLanceDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, InkyLanceDamageDeadly, InkyLanceDamageBase);

    public static int DismemberDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DismemberDamageDeadly, DismemberDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SlipperyPower>(cs, self, SlipperyAmt(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var inkBlot = new MoveStateDef(InkBlotMoveId, InkBlotMove, new SingleAttackIntent(InkBlotDamage(cs)));
        var inkyLance = new MoveStateDef(InkyLanceMoveId, InkyLanceMove, new MultiAttackIntent(InkyLanceDamage(cs), InkyLanceRepeat));
        var dismember = new MoveStateDef(DismemberMoveId, DismemberMove, new SingleAttackIntent(DismemberDamage(cs)), new StatusIntent(DismemberWounds));
        var prepare = new MoveStateDef(PrepareMoveId, PrepareMove, Intents.Buff);
        inkBlot.FollowUpState = inkyLance;
        inkyLance.FollowUpState = dismember;
        dismember.FollowUpState = prepare;
        prepare.FollowUpState = inkBlot;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { inkBlot, inkyLance, dismember, prepare }, inkBlot);
    }

    private static void DismemberMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DismemberDamage(cs)).FromMonster(self).Execute();
        CardPileCmd.AddToCombatAndPreview(cs, targets, WoundCardId, PileType.Discard, DismemberWounds, null);
    }

    private static void InkBlotMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, InkBlotDamage(cs)).FromMonster(self).Execute();
    }

    private static void InkyLanceMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, InkyLanceDamage(cs)).WithHitCount(InkyLanceRepeat).FromMonster(self).Execute();
    }

    private static void PrepareMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, PrepareStrength, self, null);
    }
}
