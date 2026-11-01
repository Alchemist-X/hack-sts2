namespace Sts2Sim.Core.Monsters;

public sealed class BattleFriendV2 : MonsterDef
{
    public const string MonsterIdEntry = "BATTLE_FRIEND_V2";
    public const string NothingMoveId = "NOTHING_MOVE";
    public const int InitialHp = 150;
    public const decimal TimeLimitAmount = 3m;

    public BattleFriendV2()
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
