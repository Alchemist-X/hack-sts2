namespace Sts2Sim.Core.Monsters;

public sealed class FrogKnightExtraState : MonsterExtraState
{
    public bool HasBeetleCharged { get; set; }

    public override MonsterExtraState DeepClone() => new FrogKnightExtraState
    {
        HasBeetleCharged = HasBeetleCharged
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.FrogKnightExtra);
        w.WriteBool(HasBeetleCharged);
    }
}

public sealed class FrogKnight : MonsterDef
{
    public const string MonsterIdEntry = "FROG_KNIGHT";
    public const string ForTheQueenMoveId = "FOR_THE_QUEEN";
    public const string StrikeDownEvilMoveId = "STRIKE_DOWN_EVIL";
    public const string TongueLashMoveId = "TONGUE_LASH";
    public const string BeetleChargeMoveId = "BEETLE_CHARGE";
    public const string HalfHealthStateId = "HALF_HEALTH";

    public const int InitialHpBase = 191;
    public const int InitialHpTough = 199;
    public const int StrikeDownEvilDamageBase = 21;
    public const int StrikeDownEvilDamageDeadly = 23;
    public const int TongueLashDamageBase = 13;
    public const int TongueLashDamageDeadly = 14;
    public const int BeetleChargeDamageBase = 35;
    public const int BeetleChargeDamageDeadly = 40;
    public const int PlatingAmountBase = 15;
    public const int PlatingAmountTough = 19;
    public const int ForTheQueenStrength = 5;
    public const int TongueLashFrail = 2;

    public FrogKnight()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int StrikeDownEvilDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StrikeDownEvilDamageDeadly, StrikeDownEvilDamageBase);

    public static int TongueLashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TongueLashDamageDeadly, TongueLashDamageBase);

    public static int BeetleChargeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BeetleChargeDamageDeadly, BeetleChargeDamageBase);

    public static int PlatingAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, PlatingAmountTough, PlatingAmountBase);

    public static FrogKnightExtraState GetExtra(MonsterState monster)
        => monster.Extra as FrogKnightExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no FrogKnight state");

    public override MonsterExtraState? CreateExtraState() => new FrogKnightExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<PlatingPower>(cs, self, PlatingAmount(cs), self, null);
        GetExtra(self).HasBeetleCharged = false;
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var forTheQueen = new MoveStateDef(ForTheQueenMoveId, ForTheQueenMove, Intents.Buff);
        var strikeDownEvil = new MoveStateDef(StrikeDownEvilMoveId, StrikeDownEvilMove, new SingleAttackIntent(StrikeDownEvilDamage(cs)));
        var tongueLash = new MoveStateDef(TongueLashMoveId, TongueLashMove, new SingleAttackIntent(TongueLashDamage(cs)), new DebuffIntent());
        var beetleCharge = new MoveStateDef(BeetleChargeMoveId, BeetleChargeMove, new SingleAttackIntent(BeetleChargeDamage(cs)));
        var halfHealth = new ConditionalBranchStateDef(HalfHealthStateId);
        halfHealth.AddState(tongueLash, static (_, m) => GetExtra(m).HasBeetleCharged || m.CurrentHp >= m.MaxHp / 2);
        halfHealth.AddState(beetleCharge, static (_, m) => !GetExtra(m).HasBeetleCharged && m.CurrentHp < m.MaxHp / 2);
        forTheQueen.FollowUpState = halfHealth;
        strikeDownEvil.FollowUpState = forTheQueen;
        tongueLash.FollowUpState = strikeDownEvil;
        beetleCharge.FollowUpState = tongueLash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { halfHealth, forTheQueen, strikeDownEvil, tongueLash, beetleCharge }, tongueLash);
    }

    private static void ForTheQueenMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, ForTheQueenStrength, self, null);
    }

    private static void StrikeDownEvilMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StrikeDownEvilDamage(cs)).FromMonster(self).Execute();
    }

    private static void TongueLashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TongueLashDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, TongueLashFrail, self, null);
    }

    private static void BeetleChargeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).HasBeetleCharged = true;
        DamageCmd.Attack(cs, BeetleChargeDamage(cs)).FromMonster(self).Execute();
    }
}
