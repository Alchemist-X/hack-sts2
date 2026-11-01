namespace Sts2Sim.Core.Monsters;

public sealed class Crusher : MonsterDef
{
    public const string MonsterIdEntry = "CRUSHER";
    public const string ThrashMoveId = "THRASH_MOVE";
    public const string EnlargingStrikeMoveId = "ENLARGING_STRIKE_MOVE";
    public const string BugStingMoveId = "BUG_STING_MOVE";
    public const string AdaptMoveId = "ADAPT_MOVE";
    public const string GuardedStrikeMoveId = "GUARDED_STRIKE_MOVE";

    public const int InitialHpBase = 209;
    public const int InitialHpTough = 219;
    public const int ThrashDamageBase = 12;
    public const int ThrashDamageDeadly = 14;
    public const int EnlargingStrikeDamageBase = 4;
    public const int EnlargingStrikeDamageDeadly = 4;
    public const int BugStingDamageBase = 6;
    public const int BugStingDamageDeadly = 7;
    public const int BugStingTimes = 2;
    public const int AdaptStrengthGainBase = 2;
    public const int AdaptStrengthGainDeadly = 3;
    public const int GuardedStrikeDamageBase = 12;
    public const int GuardedStrikeDamageDeadly = 14;
    public const int GuardedStrikeBlock = 18;
    public const int BugStingWeak = 2;
    public const int BugStingFrail = 2;

    public Crusher()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ThrashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThrashDamageDeadly, ThrashDamageBase);

    public static int EnlargingStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EnlargingStrikeDamageDeadly, EnlargingStrikeDamageBase);

    public static int BugStingDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BugStingDamageDeadly, BugStingDamageBase);

    public static int AdaptStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, AdaptStrengthGainDeadly, AdaptStrengthGainBase);

    public static int GuardedStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GuardedStrikeDamageDeadly, GuardedStrikeDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<BackAttackLeftPower>(cs, self, 1, self, null);
        PowerCmd.Apply<CrabRagePower>(cs, self, 1, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var thrash = new MoveStateDef(ThrashMoveId, ThrashMove, new SingleAttackIntent(ThrashDamage(cs)));
        var enlargingStrike = new MoveStateDef(EnlargingStrikeMoveId, EnlargingStrikeMove, new SingleAttackIntent(EnlargingStrikeDamage(cs)));
        var bugSting = new MoveStateDef(BugStingMoveId, BugStingMove, new MultiAttackIntent(BugStingDamage(cs), BugStingTimes), new DebuffIntent());
        var adapt = new MoveStateDef(AdaptMoveId, AdaptMove, Intents.Buff);
        var guardedStrike = new MoveStateDef(GuardedStrikeMoveId, GuardedStrikeMove, new SingleAttackIntent(GuardedStrikeDamage(cs)), Intents.Defend);
        thrash.FollowUpState = enlargingStrike;
        enlargingStrike.FollowUpState = bugSting;
        bugSting.FollowUpState = adapt;
        adapt.FollowUpState = guardedStrike;
        guardedStrike.FollowUpState = thrash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { thrash, enlargingStrike, bugSting, adapt, guardedStrike }, thrash);
    }

    private static void ThrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrashDamage(cs)).FromMonster(self).Execute();
    }

    private static void BugStingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BugStingDamage(cs)).WithHitCount(BugStingTimes).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, BugStingWeak, self, null);
        PowerCmd.Apply<FrailPower>(cs, targets, BugStingFrail, self, null);
    }

    private static void AdaptMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, AdaptStrengthGain(cs), self, null);
    }

    private static void EnlargingStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, EnlargingStrikeDamage(cs)).FromMonster(self).Execute();
    }

    private static void GuardedStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GuardedStrikeDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, GuardedStrikeBlock, ValueProp.Move, null);
    }
}
