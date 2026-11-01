namespace Sts2Sim.Core.Monsters;

public sealed class Osty : MonsterDef
{
    public const string MonsterIdEntry = "OSTY";
    public const string NothingMoveId = "NOTHING_MOVE";
    public const int InitialHp = 1;

    public Osty()
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

    public static bool CheckMissingWithAnim(CombatState cs, PlayerState owner) => PlayerPets.IsOstyMissing(cs, owner);

    private static void NothingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
