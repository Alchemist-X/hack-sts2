namespace Sts2Sim.Core.Monsters;

public sealed class KinFollowerExtraState : MonsterExtraState
{
    public bool StartsWithDance { get; set; }

    public override MonsterExtraState DeepClone() => new KinFollowerExtraState
    {
        StartsWithDance = StartsWithDance
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.KinFollowerExtra);
        w.WriteBool(StartsWithDance);
    }
}

public sealed class KinFollower : MonsterDef
{
    public const string MonsterIdEntry = "KIN_FOLLOWER";
    public const string QuickSlashMoveId = "QUICK_SLASH_MOVE";
    public const string BoomerangMoveId = "BOOMERANG_MOVE";
    public const string PowerDanceMoveId = "POWER_DANCE_MOVE";

    public const int MinInitialHpBase = 58;
    public const int MinInitialHpTough = 62;
    public const int MaxInitialHpBase = 59;
    public const int MaxInitialHpTough = 63;
    public const int QuickSlashDamage = 5;
    public const int BoomerangDamage = 2;
    public const int BoomerangRepeat = 2;
    public const int DanceStrengthBase = 2;
    public const int DanceStrengthDeadly = 3;

    public KinFollower()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int DanceStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DanceStrengthDeadly, DanceStrengthBase);

    public static KinFollowerExtraState GetExtra(MonsterState monster)
        => monster.Extra as KinFollowerExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no KinFollower state");

    public override MonsterExtraState? CreateExtraState() => new KinFollowerExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<MinionPower>(cs, self, 1m, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var quickSlash = new MoveStateDef(QuickSlashMoveId, QuickSlashMove, new SingleAttackIntent(QuickSlashDamage));
        var boomerang = new MoveStateDef(BoomerangMoveId, BoomerangMove, new MultiAttackIntent(BoomerangDamage, BoomerangRepeat));
        var powerDance = new MoveStateDef(PowerDanceMoveId, PowerDanceMove, Intents.Buff);
        quickSlash.FollowUpState = boomerang;
        boomerang.FollowUpState = powerDance;
        powerDance.FollowUpState = quickSlash;
        AiStateDef initial = GetExtra(self).StartsWithDance ? powerDance : quickSlash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { quickSlash, boomerang, powerDance }, initial);
    }

    private static void QuickSlashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, QuickSlashDamage).FromMonster(self).Execute();
    }

    private static void BoomerangMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BoomerangDamage).WithHitCount(BoomerangRepeat).FromMonster(self).Execute();
    }

    private static void PowerDanceMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, DanceStrength(cs), self, null);
    }
}
