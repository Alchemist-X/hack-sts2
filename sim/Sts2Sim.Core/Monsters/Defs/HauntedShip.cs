namespace Sts2Sim.Core.Monsters;

public sealed class HauntedShip : MonsterDef
{
    public const string MonsterIdEntry = "HAUNTED_SHIP";
    public const string SwipeMoveId = "SWIPE_MOVE";
    public const string StompMoveId = "STOMP_MOVE";
    public const string HauntMoveId = "HAUNT_MOVE";

    public const string DazedCardId = "DAZED";

    public const int InitialHpBase = 63;
    public const int InitialHpTough = 67;
    public const int HauntDazed = 5;
    public const int HauntWeak = 3;
    public const int SwipeDamageBase = 13;
    public const int SwipeDamageDeadly = 14;
    public const int StompDamageBase = 4;
    public const int StompDamageDeadly = 5;
    public const int StompRepeat = 3;

    public HauntedShip()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SwipeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwipeDamageDeadly, SwipeDamageBase);

    public static int StompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StompDamageDeadly, StompDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var swipe = new MoveStateDef(SwipeMoveId, SwipeMove, new SingleAttackIntent(SwipeDamage(cs)));
        var stomp = new MoveStateDef(StompMoveId, StompMove, new MultiAttackIntent(StompDamage(cs), StompRepeat));
        var haunt = new MoveStateDef(HauntMoveId, HauntMove, new DebuffIntent(), new StatusIntent(HauntDazed));
        haunt.FollowUpState = swipe;
        swipe.FollowUpState = stomp;
        stomp.FollowUpState = swipe;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { swipe, stomp, haunt }, haunt);
    }

    private static void HauntMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<WeakPower>(cs, targets, HauntWeak, self, null);
        CardPileCmd.AddToCombatAndPreview(cs, targets, DazedCardId, PileType.Discard, HauntDazed, null);
    }

    private static void StompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StompDamage(cs)).WithHitCount(StompRepeat).FromMonster(self).Execute();
    }

    private static void SwipeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwipeDamage(cs)).FromMonster(self).Execute();
    }
}
