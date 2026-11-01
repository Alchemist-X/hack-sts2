namespace Sts2Sim.Core.Monsters;

public sealed class HunterKiller : MonsterDef
{
    public const string MonsterIdEntry = "HUNTER_KILLER";
    public const string TenderizingGoopMoveId = "TENDERIZING_GOOP_MOVE";
    public const string BiteMoveId = "BITE_MOVE";
    public const string PunctureMoveId = "PUNCTURE_MOVE";
    public const string RandStateId = "RAND";

    public const int InitialHpBase = 121;
    public const int InitialHpTough = 126;
    public const int BiteDamageBase = 17;
    public const int BiteDamageDeadly = 19;
    public const int PunctureDamageBase = 7;
    public const int PunctureDamageDeadly = 8;
    public const int PunctureRepeat = 3;
    public const int TenderAmount = 1;

    public HunterKiller()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int PunctureDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PunctureDamageDeadly, PunctureDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var goop = new MoveStateDef(TenderizingGoopMoveId, GoopMove, new DebuffIntent());
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var puncture = new MoveStateDef(PunctureMoveId, PunctureMove, new MultiAttackIntent(PunctureDamage(cs), PunctureRepeat));
        var rand = new RandomBranchStateDef(RandStateId);
        goop.FollowUpState = rand;
        bite.FollowUpState = rand;
        puncture.FollowUpState = rand;
        rand.AddBranch(bite, MoveRepeatType.CannotRepeat);
        rand.AddBranch(puncture, 2);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { goop, bite, puncture, rand }, goop);
    }

    private static void GoopMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<TenderPower>(cs, targets, TenderAmount, self, null);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void PunctureMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PunctureDamage(cs)).WithHitCount(PunctureRepeat).FromMonster(self).Execute();
    }
}
