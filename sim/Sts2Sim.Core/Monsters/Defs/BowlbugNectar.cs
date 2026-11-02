namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugNectar : MonsterDef
{
    public const string MonsterIdEntry = "BOWLBUG_NECTAR";
    public const string ThrashMoveId = "THRASH_MOVE";
    public const string BuffMoveId = "BUFF_MOVE";
    public const string Thrash2MoveId = "THRASH2_MOVE";

    public const int MinInitialHpBase = 35;
    public const int MinInitialHpTough = 36;
    public const int MaxInitialHpBase = 38;
    public const int MaxInitialHpTough = 39;
    public const int ThrashDamage = 3;
    public const int BuffStrengthGainBase = 15;
    public const int BuffStrengthGainDeadly = 16;

    public BowlbugNectar()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int BuffStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BuffStrengthGainDeadly, BuffStrengthGainBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var thrash = new MoveStateDef(ThrashMoveId, ThrashMove, new SingleAttackIntent(ThrashDamage));
        var buff = new MoveStateDef(BuffMoveId, BuffMove, Intents.Buff);
        var thrash2 = new MoveStateDef(Thrash2MoveId, ThrashMove, new SingleAttackIntent(ThrashDamage));
        thrash.FollowUpState = buff;
        buff.FollowUpState = thrash2;
        thrash2.FollowUpState = thrash2;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { buff, thrash2, thrash }, thrash);
    }

    private static void ThrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrashDamage).FromMonster(self).Execute();
    }

    private static void BuffMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, BuffStrengthGain(cs), self, null);
    }
}
