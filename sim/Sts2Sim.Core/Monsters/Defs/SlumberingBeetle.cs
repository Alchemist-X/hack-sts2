namespace Sts2Sim.Core.Monsters;

public sealed class SlumberingBeetle : MonsterDef
{
    public const string MonsterIdEntry = "SLUMBERING_BEETLE";
    public const string SnoreMoveId = "SNORE_MOVE";
    public const string RolloutMoveId = "ROLL_OUT_MOVE";
    public const string SnoreNextBranchId = "SNORE_NEXT";

    public const int InitialHpBase = 86;
    public const int InitialHpTough = 89;
    public const int RolloutDamageBase = 16;
    public const int RolloutDamageDeadly = 18;
    public const int PlatingAmountBase = 15;
    public const int PlatingAmountTough = 18;
    public const int SlumberAmount = 3;
    public const int RolloutStrength = 2;

    public SlumberingBeetle()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int RolloutDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RolloutDamageDeadly, RolloutDamageBase);

    public static int PlatingAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, PlatingAmountTough, PlatingAmountBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<PlatingPower>(cs, self, PlatingAmount(cs), self, null);
        PowerCmd.Apply<SlumberPower>(cs, self, SlumberAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var snore = new MoveStateDef(SnoreMoveId, SnoreMove, Intents.Sleep);
        var rollout = new MoveStateDef(RolloutMoveId, RolloutMove, new SingleAttackIntent(RolloutDamage(cs)), Intents.Buff);
        var snoreNext = new ConditionalBranchStateDef(SnoreNextBranchId);
        snore.FollowUpState = snoreNext;
        snoreNext.AddState(snore, static (_, m) => m.HasPower<SlumberPower>());
        snoreNext.AddState(rollout, static (_, m) => !m.HasPower<SlumberPower>());
        rollout.FollowUpState = rollout;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { snore, snoreNext, rollout }, snore);
    }

    private static void SnoreMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void RolloutMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RolloutDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, RolloutStrength, self, null);
    }

    public static void WakeUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var plating = self.GetPower<PlatingPower>();
        if (plating != null)
        {
            PowerCmd.Remove(cs, plating);
        }
    }
}
