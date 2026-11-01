namespace Sts2Sim.Core.Monsters;

public sealed class Myte : MonsterDef
{
    public const string MonsterIdEntry = "MYTE";
    public const string ToxicMoveId = "TOXIC_MOVE";
    public const string BiteMoveId = "BITE_MOVE";
    public const string SuckMoveId = "SUCK_MOVE";
    public const string InitMoveId = "INIT_MOVE";

    public const string ToxicCardId = "TOXIC";
    public const string FirstSlot = "first";
    public const string SecondSlot = "second";

    public const int MinInitialHpBase = 61;
    public const int MinInitialHpTough = 64;
    public const int MaxInitialHpBase = 67;
    public const int MaxInitialHpTough = 69;
    public const int BiteDamageBase = 13;
    public const int BiteDamageDeadly = 15;
    public const int SuckDamageBase = 4;
    public const int SuckDamageDeadly = 6;
    public const int SuckStrengthBase = 2;
    public const int SuckStrengthDeadly = 3;
    public const int ToxicCount = 2;

    public Myte()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int SuckDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SuckDamageDeadly, SuckDamageBase);

    public static int SuckStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SuckStrengthDeadly, SuckStrengthBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var toxic = new MoveStateDef(ToxicMoveId, ToxicMove, new StatusIntent(ToxicCount));
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var suck = new MoveStateDef(SuckMoveId, SuckMove, new SingleAttackIntent(SuckDamage(cs)), Intents.Buff);
        var init = new ConditionalBranchStateDef(InitMoveId);
        init.AddState(toxic, static (_, m) => m.SlotName == FirstSlot);
        init.AddState(suck, static (_, m) => m.SlotName == SecondSlot);
        toxic.FollowUpState = bite;
        bite.FollowUpState = suck;
        suck.FollowUpState = toxic;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { toxic, bite, suck }, init);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void SuckMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SuckDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, SuckStrength(cs), self, null);
    }

    private static void ToxicMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, ToxicCardId, PileType.Hand, ToxicCount, null);
    }
}
