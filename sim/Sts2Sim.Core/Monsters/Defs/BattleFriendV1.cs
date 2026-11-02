namespace Sts2Sim.Core.Monsters;

public sealed class BattleFriendV1 : MonsterDef
{
    public const string MonsterIdEntry = "BATTLE_FRIEND_V1";
    public const string NothingMoveId = "NOTHING_MOVE";
    public const int InitialHp = 75;
    public const decimal TimeLimitAmount = 3m;

    public BattleFriendV1()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => InitialHp;

    public override int MaxInitialHp(CombatState cs) => InitialHp;

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var nothing = new MoveStateDef(NothingMoveId, NothingMove);
        nothing.FollowUpState = nothing;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { nothing }, nothing);
    }

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<BattlewornDummyTimeLimitPower>(cs, self, TimeLimitAmount, null, null);
    }

    private static void NothingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
