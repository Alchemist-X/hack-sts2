namespace Sts2Sim.Core.Monsters;

public sealed class CeremonialBeastExtraState : MonsterExtraState
{
    public bool IsStunnedByPlowRemoval { get; set; }

    public bool IsInSecondPhase { get; set; }

    public bool InMidCharge { get; set; }

    public override MonsterExtraState DeepClone() => new CeremonialBeastExtraState
    {
        IsStunnedByPlowRemoval = IsStunnedByPlowRemoval,
        IsInSecondPhase = IsInSecondPhase,
        InMidCharge = InMidCharge
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.CeremonialBeastExtra);
        w.WriteBool(IsStunnedByPlowRemoval);
        w.WriteBool(IsInSecondPhase);
        w.WriteBool(InMidCharge);
    }
}

public sealed class CeremonialBeast : MonsterDef
{
    public const string MonsterIdEntry = "CEREMONIAL_BEAST";
    public const string StampMoveId = "STAMP_MOVE";
    public const string PlowMoveId = "PLOW_MOVE";
    public const string StunMoveId = "STUN_MOVE";
    public const string BeastCryMoveId = "BEAST_CRY_MOVE";
    public const string StompMoveId = "STOMP_MOVE";
    public const string CrushMoveId = "CRUSH_MOVE";

    public const int HpBase = 252;
    public const int HpTough = 262;
    public const int PlowAmountBase = 150;
    public const int PlowAmountDeadly = 160;
    public const int PlowDamageBase = 18;
    public const int PlowDamageDeadly = 20;
    public const int PlowStrength = 2;
    public const int StompDamageBase = 15;
    public const int StompDamageDeadly = 17;
    public const int CrushDamageBase = 17;
    public const int CrushDamageDeadly = 19;
    public const int CrushStrengthBase = 3;
    public const int CrushStrengthDeadly = 4;
    public const int RingingAmount = 1;

    public CeremonialBeast()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int PlowAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PlowAmountDeadly, PlowAmountBase);

    public static int PlowDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PlowDamageDeadly, PlowDamageBase);

    public static int StompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StompDamageDeadly, StompDamageBase);

    public static int CrushDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, CrushDamageDeadly, CrushDamageBase);

    public static int CrushStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, CrushStrengthDeadly, CrushStrengthBase);

    public static CeremonialBeastExtraState GetExtra(MonsterState monster)
        => monster.Extra as CeremonialBeastExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no CeremonialBeast state");

    public override MonsterExtraState? CreateExtraState() => new CeremonialBeastExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var stamp = new MoveStateDef(StampMoveId, StampMove, Intents.Buff);
        var plow = new MoveStateDef(PlowMoveId, PlowMove, new SingleAttackIntent(PlowDamage(cs)), Intents.Buff);
        var stun = new MoveStateDef(StunMoveId, StunnedMove, Intents.Stun)
        {
            MustPerformOnceBeforeTransitioning = true
        };
        var beastCry = new MoveStateDef(BeastCryMoveId, BeastCryMove, new DebuffIntent());
        var stomp = new MoveStateDef(StompMoveId, StompMove, new SingleAttackIntent(StompDamage(cs)));
        var crush = new MoveStateDef(CrushMoveId, CrushMove, new SingleAttackIntent(CrushDamage(cs)), Intents.Buff);
        stamp.FollowUpState = plow;
        plow.FollowUpState = plow;
        stun.FollowUpState = beastCry;
        beastCry.FollowUpState = stomp;
        stomp.FollowUpState = crush;
        crush.FollowUpState = beastCry;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { plow, stamp, stun, beastCry, stomp, crush }, stamp);
    }

    public static void SetStunned(CombatState cs, MonsterState self)
    {
        var extra = GetExtra(self);
        extra.IsStunnedByPlowRemoval = true;
        extra.IsInSecondPhase = true;
    }

    public static void StunnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsStunnedByPlowRemoval = false;
    }

    private static void StampMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<PlowPower>(cs, self, PlowAmount(cs), self, null);
    }

    private static void PlowMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var extra = GetExtra(self);
        extra.InMidCharge = true;
        DamageCmd.Attack(cs, PlowDamage(cs)).FromMonster(self).Execute();
        extra.InMidCharge = false;
        PowerCmd.Apply<StrengthPower>(cs, self, PlowStrength, self, null);
    }

    private static void BeastCryMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<RingingPower>(cs, targets, RingingAmount, self, null);
    }

    private static void StompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StompDamage(cs)).FromMonster(self).Execute();
    }

    private static void CrushMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, CrushDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, CrushStrength(cs), self, null);
    }
}
