namespace Sts2Sim.Core.Monsters;

public sealed class SoulNexus : MonsterDef
{
    public const string MonsterIdEntry = "SOUL_NEXUS";
    public const string SoulBurnMoveId = "SOUL_BURN_MOVE";
    public const string MaelstromMoveId = "MAELSTROM_MOVE";
    public const string DrainLifeMoveId = "DRAIN_LIFE_MOVE";
    public const string RandMoveId = "RAND";

    public const int InitialHpBase = 234;
    public const int InitialHpTough = 254;
    public const int SoulBurnDamageBase = 29;
    public const int SoulBurnDamageDeadly = 31;
    public const int MaelstromDamageBase = 6;
    public const int MaelstromDamageDeadly = 7;
    public const int MaelstromRepeatBase = 4;
    public const int MaelstromRepeatDeadly = 4;
    public const int DrainLifeDamageBase = 18;
    public const int DrainLifeDamageDeadly = 19;
    public const int DrainLifeVulnerable = 2;
    public const int DrainLifeWeak = 2;

    public SoulNexus()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SoulBurnDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SoulBurnDamageDeadly, SoulBurnDamageBase);

    public static int MaelstromDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, MaelstromDamageDeadly, MaelstromDamageBase);

    public static int MaelstromRepeat(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, MaelstromRepeatDeadly, MaelstromRepeatBase);

    public static int DrainLifeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DrainLifeDamageDeadly, DrainLifeDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var soulBurn = new MoveStateDef(SoulBurnMoveId, SoulBurnMove, new SingleAttackIntent(SoulBurnDamage(cs)));
        var maelstrom = new MoveStateDef(MaelstromMoveId, MaelstromMove, new MultiAttackIntent(MaelstromDamage(cs), MaelstromRepeat(cs)));
        var drainLife = new MoveStateDef(DrainLifeMoveId, DrainLifeMove, new SingleAttackIntent(DrainLifeDamage(cs)), new DebuffIntent(strong: true));
        var rand = new RandomBranchStateDef(RandMoveId);
        soulBurn.FollowUpState = rand;
        maelstrom.FollowUpState = rand;
        drainLife.FollowUpState = rand;
        rand.AddBranch(soulBurn, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(maelstrom, MoveRepeatType.CannotRepeat, 1f);
        rand.AddBranch(drainLife, MoveRepeatType.CannotRepeat, 1f);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { soulBurn, maelstrom, drainLife, rand }, soulBurn);
    }

    private static void DrainLifeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DrainLifeDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, targets, DrainLifeVulnerable, self, null);
        PowerCmd.Apply<WeakPower>(cs, targets, DrainLifeWeak, self, null);
    }

    private static void MaelstromMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, MaelstromDamage(cs)).WithHitCount(MaelstromRepeat(cs)).FromMonster(self).Execute();
    }

    private static void SoulBurnMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SoulBurnDamage(cs)).FromMonster(self).Execute();
    }
}
