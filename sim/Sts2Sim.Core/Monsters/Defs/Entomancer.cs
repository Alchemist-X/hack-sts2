namespace Sts2Sim.Core.Monsters;

public sealed class Entomancer : MonsterDef
{
    public const string MonsterIdEntry = "ENTOMANCER";
    public const string PheromoneSpitMoveId = "PHEROMONE_SPIT_MOVE";
    public const string BeesMoveId = "BEES_MOVE";
    public const string SpearMoveId = "SPEAR_MOVE";

    public const int InitialHpBase = 145;
    public const int InitialHpTough = 165;
    public const int SpearDamageBase = 18;
    public const int SpearDamageDeadly = 20;
    public const int BeesRepeatBase = 7;
    public const int BeesRepeatDeadly = 8;
    public const int BeesDamageBase = 3;
    public const int BeesDamageDeadly = 3;
    public const int PersonalHiveAmount = 1;
    public const int PersonalHiveMax = 3;
    public const int SpitStrengthWithHive = 1;
    public const int SpitStrengthMaxHive = 2;

    public Entomancer()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SpearDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SpearDamageDeadly, SpearDamageBase);

    public static int BeesRepeat(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BeesRepeatDeadly, BeesRepeatBase);

    public static int BeesDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BeesDamageDeadly, BeesDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<PersonalHivePower>(cs, self, PersonalHiveAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var spit = new MoveStateDef(PheromoneSpitMoveId, SpitMove, Intents.Buff);
        var bees = new MoveStateDef(BeesMoveId, BeesMove, new MultiAttackIntent(BeesDamage(cs), BeesRepeat(cs)));
        var spear = new MoveStateDef(SpearMoveId, SpearMove, new SingleAttackIntent(SpearDamage(cs)));
        bees.FollowUpState = spear;
        spear.FollowUpState = spit;
        spit.FollowUpState = bees;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { spit, bees, spear }, bees);
    }

    private static void BeesMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BeesDamage(cs)).WithHitCount(BeesRepeat(cs)).FromMonster(self).Execute();
    }

    private static void SpearMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SpearDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpitMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var hive = self.GetPower<PersonalHivePower>();
        if (hive != null && hive.Amount < PersonalHiveMax)
        {
            PowerCmd.Apply<PersonalHivePower>(cs, self, PersonalHiveAmount, self, null);
            PowerCmd.Apply<StrengthPower>(cs, self, SpitStrengthWithHive, self, null);
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, self, SpitStrengthMaxHive, self, null);
    }
}
