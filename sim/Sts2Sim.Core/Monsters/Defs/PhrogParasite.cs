namespace Sts2Sim.Core.Monsters;

public sealed class PhrogParasite : MonsterDef
{
    public const string MonsterIdEntry = "PHROG_PARASITE";
    public const string InfectMoveId = "INFECT_MOVE";
    public const string LashMoveId = "LASH_MOVE";
    public const string RandMoveId = "RAND";
    public const string InfectionCardId = "INFECTION";

    public const int MinInitialHpBase = 61;
    public const int MinInitialHpTough = 66;
    public const int MaxInitialHpBase = 64;
    public const int MaxInitialHpTough = 68;
    public const int LashDamageBase = 4;
    public const int LashDamageDeadly = 5;
    public const int LashRepeat = 4;
    public const int InfestAmt = 3;
    public const int InfestedPowerAmount = 4;

    public PhrogParasite()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int LashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, LashDamageDeadly, LashDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<InfestedPower>(cs, self, InfestedPowerAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var infect = new MoveStateDef(InfectMoveId, InfectMove, new StatusIntent(InfestAmt));
        var lash = new MoveStateDef(LashMoveId, LashMove, new MultiAttackIntent(LashDamage(cs), LashRepeat));
        var rand = new RandomBranchStateDef(RandMoveId);
        infect.FollowUpState = lash;
        lash.FollowUpState = infect;
        rand.AddBranch(infect, MoveRepeatType.CannotRepeat);
        rand.AddBranch(lash, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { infect, lash, rand }, infect);
    }

    private static void InfectMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, InfectionCardId, PileType.Discard, InfestAmt, null);
    }

    private static void LashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LashDamage(cs)).WithHitCount(LashRepeat).FromMonster(self).Execute();
    }
}
