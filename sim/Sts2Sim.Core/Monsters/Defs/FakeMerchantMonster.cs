namespace Sts2Sim.Core.Monsters;

public sealed class FakeMerchantMonster : MonsterDef
{
    public const string MonsterIdEntry = "FAKE_MERCHANT_MONSTER";
    public const string SwipeMoveId = "SWIPE_MOVE";
    public const string SpewCoinsMoveId = "SPEW_COINS_MOVE";
    public const string ThrowRelicMoveId = "THROW_RELIC_MOVE";
    public const string EnrageMoveId = "ENRAGE_MOVE";
    public const string RandMoveStateId = "RAND_MOVE";
    public const string RandAttackMoveStateId = "RAND_ATTACK_MOVE";

    public const int InitialHpBase = 165;
    public const int InitialHpTough = 175;
    public const int SwipeDamageBase = 13;
    public const int SwipeDamageDeadly = 15;
    public const int ThrowRelicDamageBase = 9;
    public const int ThrowRelicDamageDeadly = 10;
    public const int SpewCoinsDamage = 2;
    public const int SpewCoinsRepeat = 8;
    public const int ThrowRelicFrail = 1;
    public const int EnrageStrength = 2;

    public FakeMerchantMonster()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public static int SwipeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SwipeDamageDeadly, SwipeDamageBase);

    public static int ThrowRelicDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThrowRelicDamageDeadly, ThrowRelicDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var swipe = new MoveStateDef(SwipeMoveId, SwipeMove, new SingleAttackIntent(SwipeDamage(cs)));
        var spewCoins = new MoveStateDef(SpewCoinsMoveId, SpewCoinsMove, new MultiAttackIntent(SpewCoinsDamage, SpewCoinsRepeat));
        var throwRelic = new MoveStateDef(ThrowRelicMoveId, ThrowRelicMove, new SingleAttackIntent(ThrowRelicDamage(cs)), new DebuffIntent());
        var enrage = new MoveStateDef(EnrageMoveId, EnrageMove, Intents.Buff);
        var rand = new RandomBranchStateDef(RandMoveStateId);
        rand.AddBranch(swipe, MoveRepeatType.CannotRepeat);
        rand.AddBranch(spewCoins, MoveRepeatType.CannotRepeat);
        rand.AddBranch(throwRelic, MoveRepeatType.CannotRepeat);
        rand.AddBranch(enrage, 3, MoveRepeatType.CannotRepeat);
        swipe.FollowUpState = rand;
        spewCoins.FollowUpState = rand;
        enrage.FollowUpState = rand;
        var randAttack = new RandomBranchStateDef(RandAttackMoveStateId);
        randAttack.AddBranch(swipe, MoveRepeatType.CannotRepeat);
        randAttack.AddBranch(spewCoins, MoveRepeatType.CannotRepeat);
        randAttack.AddBranch(throwRelic, MoveRepeatType.CannotRepeat);
        throwRelic.FollowUpState = randAttack;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { swipe, spewCoins, throwRelic, enrage, rand, randAttack }, swipe);
    }

    private static void SwipeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SwipeDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpewCoinsMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SpewCoinsDamage).FromMonster(self).WithHitCount(SpewCoinsRepeat).Execute();
    }

    private static void ThrowRelicMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrowRelicDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, ThrowRelicFrail, self, null);
    }

    private static void EnrageMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, EnrageStrength, self, null);
    }
}
