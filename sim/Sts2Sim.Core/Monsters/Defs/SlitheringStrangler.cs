namespace Sts2Sim.Core.Monsters;

public sealed class SlitheringStrangler : MonsterDef
{
    public const string MonsterIdEntry = "SLITHERING_STRANGLER";
    public const string ConstrictMoveId = "CONSTRICT";
    public const string ThwackMoveId = "THWACK";
    public const string LashMoveId = "LASH";
    public const string RandStateId = "rand";

    public const int MinInitialHpBase = 53;
    public const int MinInitialHpTough = 54;
    public const int MaxInitialHpBase = 55;
    public const int MaxInitialHpTough = 56;
    public const int ThwackDamageBase = 7;
    public const int ThwackDamageDeadly = 8;
    public const int LashDamageBase = 12;
    public const int LashDamageDeadly = 13;
    public const int ThwackBlock = 5;
    public const int ConstrictAmount = 3;

    public SlitheringStrangler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ThwackDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThwackDamageDeadly, ThwackDamageBase);

    public static int LashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LashDamageDeadly, LashDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var constrict = new MoveStateDef(ConstrictMoveId, ConstrictMove, new DebuffIntent());
        var thwack = new MoveStateDef(ThwackMoveId, ThwackMove, new SingleAttackIntent(ThwackDamage(cs)), Intents.Defend);
        var lash = new MoveStateDef(LashMoveId, LashMove, new SingleAttackIntent(LashDamage(cs)));
        var rand = new RandomBranchStateDef(RandStateId);
        constrict.FollowUpState = rand;
        thwack.FollowUpState = constrict;
        lash.FollowUpState = constrict;
        rand.AddBranch(thwack, MoveRepeatType.CanRepeatForever);
        rand.AddBranch(lash, MoveRepeatType.CanRepeatForever);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, thwack, constrict, lash }, constrict);
    }

    private static void ConstrictMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ConstrictPower>(cs, targets, ConstrictAmount, self, null);
    }

    private static void LashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LashDamage(cs)).FromMonster(self).Execute();
    }

    private static void ThwackMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThwackDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, ThwackBlock, ValueProp.Move, null);
    }
}
