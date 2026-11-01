namespace Sts2Sim.Core.Monsters;

public sealed class Tunneler : MonsterDef
{
    public const string MonsterIdEntry = "TUNNELER";
    public const string BiteMoveId = "BITE_MOVE";
    public const string BurrowMoveId = "BURROW_MOVE";
    public const string BelowMoveId = "BELOW_MOVE";
    public const string DizzyMoveId = "DIZZY_MOVE";

    public const int InitialHpBase = 87;
    public const int InitialHpTough = 92;
    public const int BiteDamageBase = 13;
    public const int BiteDamageDeadly = 15;
    public const int BlockGainBase = 32;
    public const int BlockGainTough = 37;
    public const int BelowDamageBase = 23;
    public const int BelowDamageDeadly = 26;

    public Tunneler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int BlockGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, BlockGainTough, BlockGainBase);

    public static int BelowDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BelowDamageDeadly, BelowDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var burrow = new MoveStateDef(BurrowMoveId, BurrowMove, Intents.Buff, Intents.Defend);
        var below = new MoveStateDef(BelowMoveId, BelowMove, new SingleAttackIntent(BelowDamage(cs)));
        var dizzy = new MoveStateDef(DizzyMoveId, StillDizzyMove, Intents.Stun);
        bite.FollowUpState = burrow;
        burrow.FollowUpState = below;
        below.FollowUpState = below;
        dizzy.FollowUpState = bite;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { bite, burrow, below, dizzy }, bite);
    }

    private static void BelowMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BelowDamage(cs)).FromMonster(self).Execute();
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void BurrowMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply(cs, BurrowedPower.PowerId, self, 1m, self, null);
        CreatureCmd.GainBlock(cs, self, BlockGain(cs), ValueProp.Move, null);
    }

    public static void GetStunned(CombatState cs, MonsterState self)
    {
    }

    public static void StillDizzyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
