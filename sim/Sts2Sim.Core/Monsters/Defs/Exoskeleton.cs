namespace Sts2Sim.Core.Monsters;

public sealed class Exoskeleton : MonsterDef
{
    public const string MonsterIdEntry = "EXOSKELETON";
    public const string InitMoveId = "INIT_MOVE";
    public const string RandStateId = "RAND";
    public const string SkitterMoveId = "SKITTER_MOVE";
    public const string MandiblesMoveId = "MANDIBLES_MOVE";
    public const string EnrageMoveId = "ENRAGE_MOVE";

    public const string FirstSlot = "first";
    public const string SecondSlot = "second";
    public const string ThirdSlot = "third";
    public const string FourthSlot = "fourth";

    public const int MinInitialHpBase = 24;
    public const int MinInitialHpTough = 26;
    public const int MaxInitialHpBase = 28;
    public const int MaxInitialHpTough = 30;
    public const int SkitterDamage = 1;
    public const int SkitterRepeatsBase = 3;
    public const int SkitterRepeatsDeadly = 4;
    public const int MandiblesDamageBase = 8;
    public const int MandiblesDamageDeadly = 9;
    public const int BuffAmount = 2;
    public const int HardToKillAmount = 9;

    public Exoskeleton()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SkitterRepeats(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SkitterRepeatsDeadly, SkitterRepeatsBase);

    public static int MandiblesDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, MandiblesDamageDeadly, MandiblesDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<HardToKillPower>(cs, self, HardToKillAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var skitter = new MoveStateDef(SkitterMoveId, SkitterMove, new MultiAttackIntent(SkitterDamage, SkitterRepeats(cs)));
        var mandibles = new MoveStateDef(MandiblesMoveId, MandiblesMove, new SingleAttackIntent(MandiblesDamage(cs)));
        var enrage = new MoveStateDef(EnrageMoveId, EnrageMove, Intents.Buff);
        var rand = new RandomBranchStateDef(RandStateId);
        rand.AddBranch(skitter, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(mandibles, MoveRepeatType.CannotRepeat, 1f);
        var init = new ConditionalBranchStateDef(InitMoveId);
        init.AddState(skitter, static (_, m) => m.SlotName == FirstSlot);
        init.AddState(mandibles, static (_, m) => m.SlotName == SecondSlot);
        init.AddState(enrage, static (_, m) => m.SlotName == ThirdSlot);
        init.AddState(rand, static (_, m) => m.SlotName == FourthSlot);
        skitter.FollowUpState = rand;
        mandibles.FollowUpState = enrage;
        enrage.FollowUpState = rand;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { init, rand, skitter, mandibles, enrage }, init);
    }

    private static void SkitterMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SkitterDamage).WithHitCount(SkitterRepeats(cs)).FromMonster(self).Execute();
    }

    private static void MandiblesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, MandiblesDamage(cs)).FromMonster(self).Execute();
    }

    private static void EnrageMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, BuffAmount, self, null);
    }
}
