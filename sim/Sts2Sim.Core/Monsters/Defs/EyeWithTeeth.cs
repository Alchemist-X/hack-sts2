namespace Sts2Sim.Core.Monsters;

public sealed class EyeWithTeeth : MonsterDef
{
    public const string MonsterIdEntry = "EYE_WITH_TEETH";
    public const string DistractMoveId = "DISTRACT_MOVE";
    public const string IllusionPowerId = "ILLUSION_POWER";
    public const string DazedCardId = "DAZED";

    public const int Hp = 6;
    public const int DistractAmount = 3;
    public const int IllusionAmount = 1;

    public EyeWithTeeth()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => Hp;

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply(cs, IllusionPowerId, self, IllusionAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var distract = new MoveStateDef(DistractMoveId, DistractMove, new StatusIntent(DistractAmount));
        distract.FollowUpState = distract;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { distract }, distract);
    }

    private static void DistractMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, DazedCardId, PileType.Discard, DistractAmount, null);
    }
}
