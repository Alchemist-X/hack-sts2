namespace Sts2Sim.Core.Monsters;

public sealed class MysteriousKnight : MonsterDef
{
    public const string MonsterIdEntry = "MYSTERIOUS_KNIGHT";
    public const string WarChantMoveId = "WAR_CHANT";
    public const string FlailMoveId = "FLAIL_MOVE";
    public const string RamMoveId = "RAM_MOVE";
    public const string RandStateId = "RAND";

    public const int InitialHpBase = 101;
    public const int InitialHpTough = 108;
    public const int FlailDamageBase = 9;
    public const int FlailDamageDeadly = 10;
    public const int FlailRepeat = 2;
    public const int RamDamageBase = 15;
    public const int RamDamageDeadly = 17;
    public const int WarChantStrength = 3;
    public const int StartingStrength = 6;
    public const int StartingPlating = 6;

    public MysteriousKnight()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int FlailDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FlailDamageDeadly, FlailDamageBase);

    public static int RamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RamDamageDeadly, RamDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, StartingStrength, self, null);
        PowerCmd.Apply<PlatingPower>(cs, self, StartingPlating, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var warChant = new MoveStateDef(WarChantMoveId, WarChantMove, Intents.Buff);
        var flail = new MoveStateDef(FlailMoveId, FlailMove, new MultiAttackIntent(FlailDamage(cs), FlailRepeat));
        var ram = new MoveStateDef(RamMoveId, RamMove, new SingleAttackIntent(RamDamage(cs)));
        var rand = new RandomBranchStateDef(RandStateId);
        warChant.FollowUpState = rand;
        flail.FollowUpState = rand;
        ram.FollowUpState = rand;
        rand.AddBranch(warChant, MoveRepeatType.CannotRepeat);
        rand.AddBranch(flail, 2);
        rand.AddBranch(ram, 2);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { warChant, flail, ram, rand }, ram);
    }

    private static void FlailMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FlailDamage(cs)).WithHitCount(FlailRepeat).FromMonster(self).Execute();
    }

    private static void RamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RamDamage(cs)).FromMonster(self).Execute();
    }

    private static void WarChantMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, WarChantStrength, self, null);
    }
}
