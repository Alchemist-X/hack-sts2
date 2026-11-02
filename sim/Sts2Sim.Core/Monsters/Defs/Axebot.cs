namespace Sts2Sim.Core.Monsters;

public sealed class AxebotExtraState : MonsterExtraState
{
    public int? StockOverrideAmount { get; set; }

    public override MonsterExtraState DeepClone() => new AxebotExtraState
    {
        StockOverrideAmount = StockOverrideAmount
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.AxebotExtra);
        w.WriteBool(StockOverrideAmount.HasValue);
        w.WriteInt32(StockOverrideAmount ?? 0);
    }
}

public sealed class Axebot : MonsterDef
{
    public const string MonsterIdEntry = "AXEBOT";
    public const string BootUpMoveId = "BOOT_UP_MOVE";
    public const string OneTwoMoveId = "ONE_TWO_MOVE";
    public const string HammerUppercutMoveId = "HAMMER_UPPERCUT_MOVE";

    public const int OneTwoRepeat = 2;
    public const int InitialStockAmount = 2;
    public const int RespawnMaxHpBonusPerRespawn = 10;
    public const int MinInitialHpBase = 70;
    public const int MinInitialHpTough = 76;
    public const int MaxInitialHpBase = 78;
    public const int MaxInitialHpTough = 86;
    public const int BootUpBlockBase = 10;
    public const int BootUpBlockDeadly = 15;
    public const int OneTwoDamageBase = 10;
    public const int OneTwoDamageDeadly = 11;
    public const int BootUpStrGainBase = 3;
    public const int BootUpStrGainDeadly = 4;
    public const int HammerUppercutDamageBase = 14;
    public const int HammerUppercutDamageDeadly = 18;
    public const int HammerUppercutDebuff = 2;

    public Axebot()
        : base(MonsterIdEntry)
    {
    }

    public static AxebotExtraState GetExtra(MonsterState monster)
        => monster.Extra as AxebotExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Axebot state");

    public override MonsterExtraState? CreateExtraState() => new AxebotExtraState();

    public static int StockAmount(MonsterState self) => GetExtra(self).StockOverrideAmount ?? InitialStockAmount;

    public static int RespawnCount(MonsterState self) => InitialStockAmount - StockAmount(self);

    public static int RespawnMaxHpBonus(MonsterState self) => RespawnCount(self) * RespawnMaxHpBonusPerRespawn;

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public override int MinInitialHp(CombatState cs, MonsterState self) => MinInitialHp(cs) + RespawnMaxHpBonus(self);

    public override int MaxInitialHp(CombatState cs, MonsterState self) => MaxInitialHp(cs) + RespawnMaxHpBonus(self);

    public static int BootUpBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BootUpBlockDeadly, BootUpBlockBase);

    public static int OneTwoDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, OneTwoDamageDeadly, OneTwoDamageBase);

    public static int BootUpStrGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BootUpStrGainDeadly, BootUpStrGainBase);

    public static int HammerUppercutDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HammerUppercutDamageDeadly, HammerUppercutDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        if (StockAmount(self) > 0)
        {
            PowerCmd.Apply<StockPower>(cs, self, StockAmount(self), null, null);
        }
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var bootUp = new MoveStateDef(BootUpMoveId, BootUpMove, Intents.Defend, Intents.Buff);
        var oneTwo = new MoveStateDef(OneTwoMoveId, OneTwoMove, new MultiAttackIntent(OneTwoDamage(cs), OneTwoRepeat));
        var hammerUppercut = new MoveStateDef(HammerUppercutMoveId, HammerUppercutMove, new SingleAttackIntent(HammerUppercutDamage(cs)), new DebuffIntent());
        bootUp.FollowUpState = hammerUppercut;
        hammerUppercut.FollowUpState = oneTwo;
        oneTwo.FollowUpState = hammerUppercut;
        var states = new AiStateDef[] { bootUp, oneTwo, hammerUppercut };
        if (GetExtra(self).StockOverrideAmount.HasValue)
        {
            return new MonsterMoveStateMachineDef(states, bootUp);
        }
        return new MonsterMoveStateMachineDef(states, hammerUppercut);
    }

    private static void BootUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, BootUpBlock(cs), ValueProp.Move, null);
        PowerCmd.Apply<StrengthPower>(cs, self, BootUpStrGain(cs) * RespawnCount(self), self, null);
    }

    private static void OneTwoMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, OneTwoDamage(cs)).WithHitCount(OneTwoRepeat).FromMonster(self).Execute();
    }

    private static void HammerUppercutMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HammerUppercutDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, HammerUppercutDebuff, self, null);
        PowerCmd.Apply<FrailPower>(cs, targets, HammerUppercutDebuff, self, null);
    }
}
