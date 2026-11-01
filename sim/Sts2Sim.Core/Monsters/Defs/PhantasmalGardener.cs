namespace Sts2Sim.Core.Monsters;

public sealed class PhantasmalGardener : MonsterDef
{
    public const string MonsterIdEntry = "PHANTASMAL_GARDENER";
    public const string BiteMoveId = "BITE_MOVE";
    public const string LashMoveId = "LASH_MOVE";
    public const string FlailMoveId = "FLAIL_MOVE";
    public const string EnlargeMoveId = "ENLARGE_MOVE";
    public const string InitMoveId = "INIT_MOVE";

    public const string FirstSlot = "first";
    public const string SecondSlot = "second";
    public const string ThirdSlot = "third";
    public const string FourthSlot = "fourth";

    public const int MinInitialHpBase = 26;
    public const int MinInitialHpTough = 27;
    public const int MaxInitialHpBase = 31;
    public const int MaxInitialHpTough = 32;
    public const int BiteDamage = 5;
    public const int LashDamage = 7;
    public const int FlailDamage = 1;
    public const int FlailRepeat = 3;
    public const int EnlargeStrBase = 2;
    public const int EnlargeStrDeadly = 3;
    public const int SkittishAmountBase = 6;
    public const int SkittishAmountTough = 7;

    public PhantasmalGardener()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int EnlargeStr(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EnlargeStrDeadly, EnlargeStrBase);

    public static int SkittishAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, SkittishAmountTough, SkittishAmountBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SkittishPower>(cs, self, SkittishAmount(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage));
        var lash = new MoveStateDef(LashMoveId, LashMove, new SingleAttackIntent(LashDamage));
        var flail = new MoveStateDef(FlailMoveId, FlailMove, new MultiAttackIntent(FlailDamage, FlailRepeat));
        var enlarge = new MoveStateDef(EnlargeMoveId, EnlargeMove, Intents.Buff);
        var init = new ConditionalBranchStateDef(InitMoveId);
        init.AddState(flail, static (_, m) => m.SlotName == FirstSlot);
        init.AddState(bite, static (_, m) => m.SlotName == SecondSlot);
        init.AddState(lash, static (_, m) => m.SlotName == ThirdSlot);
        init.AddState(enlarge, static (_, m) => m.SlotName == FourthSlot);
        bite.FollowUpState = lash;
        lash.FollowUpState = flail;
        flail.FollowUpState = enlarge;
        enlarge.FollowUpState = bite;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { bite, lash, enlarge, flail }, init);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage).FromMonster(self).Execute();
    }

    private static void EnlargeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, EnlargeStr(cs), self, null);
    }

    private static void FlailMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FlailDamage).WithHitCount(FlailRepeat).FromMonster(self).Execute();
    }

    private static void LashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, LashDamage).FromMonster(self).Execute();
    }
}
