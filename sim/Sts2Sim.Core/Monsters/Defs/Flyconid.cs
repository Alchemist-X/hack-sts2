namespace Sts2Sim.Core.Monsters;

public sealed class Flyconid : MonsterDef
{
    public const string MonsterIdEntry = "FLYCONID";
    public const string VulnerableSporesMoveId = "VULNERABLE_SPORES_MOVE";
    public const string FrailSporesMoveId = "FRAIL_SPORES_MOVE";
    public const string SmashMoveId = "SMASH_MOVE";
    public const string RandStateId = "RAND";
    public const string InitialStateId = "INITIAL";

    public const int MinInitialHpBase = 47;
    public const int MinInitialHpTough = 51;
    public const int MaxInitialHpBase = 49;
    public const int MaxInitialHpTough = 53;
    public const int SmashDamageBase = 11;
    public const int SmashDamageDeadly = 12;
    public const int SporeDamageBase = 8;
    public const int SporeDamageDeadly = 9;
    public const int FrailAmount = 2;
    public const int VulnerableAmount = 2;

    public Flyconid()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SmashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SmashDamageDeadly, SmashDamageBase);

    public static int SporeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SporeDamageDeadly, SporeDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var vulnerableSpores = new MoveStateDef(VulnerableSporesMoveId, VulnerableSporesMove, new DebuffIntent());
        var frailSpores = new MoveStateDef(FrailSporesMoveId, FrailSporesMove, new SingleAttackIntent(SporeDamage(cs)), new DebuffIntent());
        var smash = new MoveStateDef(SmashMoveId, SmashMove, new SingleAttackIntent(SmashDamage(cs)));
        var rand = new RandomBranchStateDef(RandStateId);
        var initial = new RandomBranchStateDef(InitialStateId);
        vulnerableSpores.FollowUpState = rand;
        frailSpores.FollowUpState = rand;
        smash.FollowUpState = rand;
        rand.AddBranch(vulnerableSpores, 3, MoveRepeatType.CannotRepeat);
        rand.AddBranch(frailSpores, 2, MoveRepeatType.CannotRepeat);
        rand.AddBranch(smash, MoveRepeatType.CannotRepeat);
        initial.AddBranch(frailSpores, 2, MoveRepeatType.CannotRepeat);
        initial.AddBranch(smash, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { vulnerableSpores, frailSpores, smash, rand, initial }, initial);
    }

    private static void FrailSporesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SporeDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, FrailAmount, self, null);
    }

    private static void SmashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SmashDamage(cs)).FromMonster(self).Execute();
    }

    private static void VulnerableSporesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<VulnerablePower>(cs, targets, VulnerableAmount, self, null);
    }
}
