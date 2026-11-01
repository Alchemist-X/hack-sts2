namespace Sts2Sim.Core.Monsters;

public sealed class TheLost : MonsterDef
{
    public const string MonsterIdEntry = "THE_LOST";
    public const string DebilitatingSmogMoveId = "DEBILITATING_SMOG";
    public const string EyeLasersMoveId = "EYE_LASERS";

    public const int InitialHpBase = 93;
    public const int InitialHpTough = 99;
    public const int EyeLasersDamageBase = 4;
    public const int EyeLasersDamageDeadly = 5;
    public const int EyeLasersRepeat = 2;
    public const int StrengthStealAmountBase = 2;
    public const int StrengthStealAmountDeadly = 2;

    public TheLost()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int EyeLasersDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EyeLasersDamageDeadly, EyeLasersDamageBase);

    public static int DebilitatingSmogStrengthStealAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StrengthStealAmountDeadly, StrengthStealAmountBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply(cs, PossessStrengthPower.PowerId, self, 1m, null, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var smog = new MoveStateDef(DebilitatingSmogMoveId, DebilitatingSmogMove, new DebuffIntent(), Intents.Buff);
        var eyeLasers = new MoveStateDef(EyeLasersMoveId, EyeLasersMove, new MultiAttackIntent(EyeLasersDamage(cs), EyeLasersRepeat));
        smog.FollowUpState = eyeLasers;
        eyeLasers.FollowUpState = smog;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { smog, eyeLasers }, smog);
    }

    private static void DebilitatingSmogMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, targets, -DebilitatingSmogStrengthStealAmount(cs), self, null);
        PowerCmd.Apply<StrengthPower>(cs, self, DebilitatingSmogStrengthStealAmount(cs), self, null);
    }

    private static void EyeLasersMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, EyeLasersDamage(cs)).WithHitCount(EyeLasersRepeat).FromMonster(self).Execute();
    }
}
