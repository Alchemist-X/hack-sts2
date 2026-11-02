namespace Sts2Sim.Core.Monsters;

public sealed class Architect : MonsterDef
{
    public const string MonsterIdEntry = "ARCHITECT";
    public const string NothingMoveId = "NOTHING";

    public const int MinInitialHpBase = 9999;
    public const int MaxInitialHpBase = 9999;

    public Architect()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => MinInitialHpBase;

    public override int MaxInitialHp(CombatState cs) => MaxInitialHpBase;

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var nothing = new MoveStateDef(NothingMoveId, static (_, _, _) => { }, Intents.Hidden);
        nothing.FollowUpState = nothing;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { nothing }, nothing);
    }
}
