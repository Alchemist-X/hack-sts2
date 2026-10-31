namespace Sts2Sim.Core.Monsters;

public sealed class FuzzyWurmCrawler : MonsterDef
{
    public const string MonsterIdEntry = "FUZZY_WURM_CRAWLER";
    public const string FirstAcidGoopMoveId = "FIRST_ACID_GOOP";
    public const string AcidGoopMoveId = "ACID_GOOP";
    public const string InhaleMoveId = "INHALE";

    public const int MinInitialHpBase = 55;
    public const int MinInitialHpTough = 58;
    public const int MaxInitialHpBase = 57;
    public const int MaxInitialHpTough = 59;
    public const int AcidGoopDamageBase = 4;
    public const int AcidGoopDamageDeadly = 6;
    public const int InhaleStrengthGain = 7;

    public FuzzyWurmCrawler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int AcidGoopDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, AcidGoopDamageDeadly, AcidGoopDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var firstAcidGoop = new MoveStateDef(FirstAcidGoopMoveId, AcidGoopMove, new SingleAttackIntent(AcidGoopDamage(cs)));
        var acidGoop = new MoveStateDef(AcidGoopMoveId, AcidGoopMove, new SingleAttackIntent(AcidGoopDamage(cs)));
        var inhale = new MoveStateDef(InhaleMoveId, InhaleMove, Intents.Buff);
        firstAcidGoop.FollowUpState = inhale;
        inhale.FollowUpState = acidGoop;
        acidGoop.FollowUpState = firstAcidGoop;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { firstAcidGoop, acidGoop, inhale }, firstAcidGoop);
    }

    private static void AcidGoopMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, AcidGoopDamage(cs)).FromMonster(self).Execute();
    }

    private static void InhaleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, InhaleStrengthGain, self, null);
    }
}
