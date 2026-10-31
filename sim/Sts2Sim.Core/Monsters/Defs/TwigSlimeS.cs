namespace Sts2Sim.Core.Monsters;

public sealed class TwigSlimeS : MonsterDef
{
    public const string MonsterIdEntry = "TWIG_SLIME_S";
    public const string TackleMoveId = "TACKLE_MOVE";

    public const int MinInitialHpBase = 7;
    public const int MinInitialHpTough = 8;
    public const int MaxInitialHpBase = 11;
    public const int MaxInitialHpTough = 12;
    public const int TackleDamageBase = 4;
    public const int TackleDamageDeadly = 5;

    public TwigSlimeS()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int TackleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TackleDamageDeadly, TackleDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var tackle = new MoveStateDef(TackleMoveId, TackleMove, new SingleAttackIntent(TackleDamage(cs)));
        tackle.FollowUpState = tackle;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { tackle }, tackle);
    }

    private static void TackleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TackleDamage(cs)).FromMonster(self).Execute();
    }
}
