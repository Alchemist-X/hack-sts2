namespace Sts2Sim.Core.Monsters;

public sealed class Byrdonis : MonsterDef
{
    public const string MonsterIdEntry = "BYRDONIS";
    public const string PeckMoveId = "PECK_MOVE";
    public const string SwoopMoveId = "SWOOP_MOVE";

    public const int MinInitialHpBase = 81;
    public const int MinInitialHpTough = 90;
    public const int MaxInitialHpBase = 84;
    public const int MaxInitialHpTough = 90;
    public const int PeckDamageBase = 3;
    public const int PeckDamageDeadly = 4;
    public const int PeckRepeatBase = 3;
    public const int PeckRepeatDeadly = 3;
    public const int SwoopDamageBase = 17;
    public const int SwoopDamageDeadly = 19;
    public const int TerritorialAmount = 1;

    public Byrdonis()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int PeckDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PeckDamageDeadly, PeckDamageBase);

    public static int PeckRepeat(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PeckRepeatDeadly, PeckRepeatBase);

    public static int SwoopDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwoopDamageDeadly, SwoopDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<TerritorialPower>(cs, self, TerritorialAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var peck = new MoveStateDef(PeckMoveId, PeckMove, new MultiAttackIntent(PeckDamage(cs), PeckRepeat(cs)));
        var swoop = new MoveStateDef(SwoopMoveId, SwoopMove, new SingleAttackIntent(SwoopDamage(cs)));
        swoop.FollowUpState = peck;
        peck.FollowUpState = swoop;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { swoop, peck }, swoop);
    }

    private static void PeckMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PeckDamage(cs)).WithHitCount(PeckRepeat(cs)).FromMonster(self).Execute();
    }

    private static void SwoopMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwoopDamage(cs)).FromMonster(self).Execute();
    }
}
