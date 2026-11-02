namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugEgg : MonsterDef
{
    public const string MonsterIdEntry = "BOWLBUG_EGG";
    public const string BiteMoveId = "BITE_MOVE";

    public const int MinInitialHpBase = 21;
    public const int MinInitialHpTough = 23;
    public const int MaxInitialHpBase = 22;
    public const int MaxInitialHpTough = 24;
    public const int BiteDamageBase = 7;
    public const int BiteDamageDeadly = 8;
    public const int ProtectBlockBase = 7;
    public const int ProtectBlockDeadly = 8;

    public BowlbugEgg()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int ProtectBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ProtectBlockDeadly, ProtectBlockBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)), Intents.Defend);
        bite.FollowUpState = bite;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { bite }, bite);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, ProtectBlock(cs), ValueProp.Move, null);
    }
}
