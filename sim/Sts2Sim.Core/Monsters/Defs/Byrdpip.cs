namespace Sts2Sim.Core.Monsters;

public sealed class Byrdpip : MonsterDef
{
    public const string MonsterIdEntry = "BYRDPIP";
    public const string NothingMoveId = "NOTHING_MOVE";
    public const int InitialHp = 9999;

    public Byrdpip()
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

    private static void NothingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
