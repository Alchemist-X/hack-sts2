namespace Sts2Sim.Core.Monsters;

public sealed class TwigSlimeM : MonsterDef
{
    public const string MonsterIdEntry = "TWIG_SLIME_M";
    public const string PokeyPounceMoveId = "POKEY_POUNCE_MOVE";
    public const string StickyShotMoveId = "STICKY_SHOT_MOVE";
    public const string RandId = "RAND";

    public const int MinInitialHpBase = 26;
    public const int MinInitialHpTough = 27;
    public const int MaxInitialHpBase = 28;
    public const int MaxInitialHpTough = 29;
    public const int ClumpDamageBase = 11;
    public const int ClumpDamageDeadly = 12;
    public const int StickyAmount = 1;
    public const int PokeyPounceMaxRepeats = 2;

    public TwigSlimeM()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ClumpDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ClumpDamageDeadly, ClumpDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var pokey = new MoveStateDef(PokeyPounceMoveId, ClumpShotMove, new SingleAttackIntent(ClumpDamage(cs)));
        var sticky = new MoveStateDef(StickyShotMoveId, StickyShotMove, new StatusIntent(StickyAmount));
        var rand = new RandomBranchStateDef(RandId);
        pokey.FollowUpState = rand;
        sticky.FollowUpState = rand;
        rand.AddBranch(pokey, PokeyPounceMaxRepeats);
        rand.AddBranch(sticky, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { pokey, sticky, rand }, sticky);
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
