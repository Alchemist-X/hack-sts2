namespace Sts2Sim.Core.Monsters;

public sealed class LeafSlimeM : MonsterDef
{
    public const string MonsterIdEntry = "LEAF_SLIME_M";
    public const string ClumpShotMoveId = "CLUMP_SHOT";
    public const string StickyShotMoveId = "STICKY_SHOT";

    public const int MinInitialHpBase = 32;
    public const int MinInitialHpTough = 33;
    public const int MaxInitialHpBase = 35;
    public const int MaxInitialHpTough = 36;
    public const int ClumpDamageBase = 8;
    public const int ClumpDamageDeadly = 9;
    public const int StickyAmount = 2;

    public LeafSlimeM()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ClumpDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ClumpDamageDeadly, ClumpDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var clump = new MoveStateDef(ClumpShotMoveId, ClumpShotMove, new SingleAttackIntent(ClumpDamage(cs)));
        var sticky = new MoveStateDef(StickyShotMoveId, StickyShotMove, new StatusIntent(StickyAmount));
        sticky.FollowUpState = clump;
        clump.FollowUpState = sticky;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { clump, sticky }, sticky);
    }

    private static void ClumpShotMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ClumpDamage(cs)).FromMonster(self).Execute();
    }

    private static void StickyShotMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, StickyAmount, null);
    }
}
