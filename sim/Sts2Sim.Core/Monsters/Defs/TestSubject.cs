namespace Sts2Sim.Core.Monsters;

public sealed class TestSubjectExtraState : MonsterExtraState
{
    public int Respawns { get; set; }

    public int ExtraMultiClawCount { get; set; }

    public override MonsterExtraState DeepClone() => new TestSubjectExtraState
    {
        Respawns = Respawns,
        ExtraMultiClawCount = ExtraMultiClawCount
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.TestSubjectExtra);
        w.WriteInt32(Respawns);
        w.WriteInt32(ExtraMultiClawCount);
    }
}

public sealed class TestSubject : MonsterDef
{
    public const string MonsterIdEntry = "TEST_SUBJECT";
    public const string RespawnMoveId = "RESPAWN_MOVE";
    public const string BiteMoveId = "BITE_MOVE";
    public const string SkullBashMoveId = "SKULL_BASH_MOVE";
    public const string MultiClawMoveId = "MULTI_CLAW_MOVE";
    public const string Phase3LacerateMoveId = "PHASE3_LACERATE_MOVE";
    public const string BigPounceMoveId = "BIG_POUNCE";
    public const string BurningGrowlMoveId = "BURNING_GROWL_MOVE";
    public const string ReviveBranchId = "REVIVE_BRANCH";
    public const string BurnCardId = "BURN";

    public const int Phase3LacerateRepeat = 3;
    public const int FirstFormHpBase = 100;
    public const int FirstFormHpTough = 111;
    public const int SecondFormHpBase = 200;
    public const int SecondFormHpTough = 212;
    public const int ThirdFormHpBase = 300;
    public const int ThirdFormHpTough = 313;
    public const int EnrageAmountBase = 2;
    public const int EnrageAmountDeadly = 3;
    public const int BiteDamageBase = 20;
    public const int BiteDamageDeadly = 22;
    public const int SkullBashDamageBase = 14;
    public const int SkullBashDamageDeadly = 16;
    public const int SkullBashVulnerable = 1;
    public const int MultiClawDamageBase = 10;
    public const int MultiClawDamageDeadly = 11;
    public const int BaseMultiClawCount = 3;
    public const int Phase3LacerateDamageBase = 10;
    public const int Phase3LacerateDamageDeadly = 11;
    public const int BigPounceDamage = 45;
    public const int BurningGrowlBurnCountBase = 3;
    public const int BurningGrowlBurnCountDeadly = 5;
    public const int BurningGrowlStrengthGainBase = 2;
    public const int BurningGrowlStrengthGainDeadly = 3;
    public const int RespawnsBeforeFinalForm = 2;

    public TestSubject()
        : base(MonsterIdEntry)
    {
    }

    public static int FirstFormHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, FirstFormHpTough, FirstFormHpBase);

    public static int SecondFormHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, SecondFormHpTough, SecondFormHpBase);

    public static int ThirdFormHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, ThirdFormHpTough, ThirdFormHpBase);

    public override int MinInitialHp(CombatState cs) => FirstFormHp(cs);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int EnrageAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EnrageAmountDeadly, EnrageAmountBase);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int SkullBashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SkullBashDamageDeadly, SkullBashDamageBase);

    public static int MultiClawDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, MultiClawDamageDeadly, MultiClawDamageBase);

    public static int Phase3LacerateDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, Phase3LacerateDamageDeadly, Phase3LacerateDamageBase);

    public static int BurningGrowlBurnCount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BurningGrowlBurnCountDeadly, BurningGrowlBurnCountBase);

    public static int BurningGrowlStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BurningGrowlStrengthGainDeadly, BurningGrowlStrengthGainBase);

    public static TestSubjectExtraState GetExtra(MonsterState monster)
        => monster.Extra as TestSubjectExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no TestSubject state");

    public static int MultiClawTotalCount(MonsterState self) => BaseMultiClawCount + GetExtra(self).ExtraMultiClawCount;

    public override MonsterExtraState? CreateExtraState() => new TestSubjectExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<AdaptablePower>(cs, self, 1, self, null);
        PowerCmd.Apply<EnragePower>(cs, self, EnrageAmount(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var dead = new MoveStateDef(RespawnMoveId, RespawnMove, Intents.Heal, Intents.Buff)
        {
            MustPerformOnceBeforeTransitioning = true
        };
        var bite = new MoveStateDef(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var skullBash = new MoveStateDef(SkullBashMoveId, SkullBashMove, new SingleAttackIntent(SkullBashDamage(cs)), new DebuffIntent());
        var multiClaw = new MoveStateDef(MultiClawMoveId, MultiClawMove, new MultiAttackIntent(MultiClawDamage(cs), static (_, m) => MultiClawTotalCount(m)));
        var lacerate = new MoveStateDef(Phase3LacerateMoveId, Phase3LacerateMove, new MultiAttackIntent(Phase3LacerateDamage(cs), Phase3LacerateRepeat));
        var bigPounce = new MoveStateDef(BigPounceMoveId, BigPounceMove, new SingleAttackIntent(BigPounceDamage));
        var burningGrowl = new MoveStateDef(BurningGrowlMoveId, BurningGrowlMove, new StatusIntent(BurningGrowlBurnCount(cs)), Intents.Buff);
        var reviveBranch = new ConditionalBranchStateDef(ReviveBranchId);
        bite.FollowUpState = skullBash;
        skullBash.FollowUpState = bite;
        multiClaw.FollowUpState = multiClaw;
        lacerate.FollowUpState = bigPounce;
        bigPounce.FollowUpState = burningGrowl;
        burningGrowl.FollowUpState = lacerate;
        dead.FollowUpState = reviveBranch;
        reviveBranch.AddState(multiClaw, static (_, m) => GetExtra(m).Respawns < RespawnsBeforeFinalForm);
        reviveBranch.AddState(lacerate, static (_, m) => GetExtra(m).Respawns >= RespawnsBeforeFinalForm);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { dead, bite, skullBash, multiClaw, lacerate, bigPounce, burningGrowl, reviveBranch }, bite);
    }

    public static void TriggerDeadState(CombatState cs, MonsterState self)
    {
        cs.Run.ExtraFields.TestSubjectKills++;
        if (self.Ai.Machine.TryGet(RespawnMoveId, out var state) && state is MoveStateDef dead)
        {
            MonsterLifecycle.SetMoveImmediate(self, dead, forceTransition: true);
        }
    }

    private static void Revive(CombatState cs, MonsterState self, int baseRespawnHp)
    {
        decimal scaledHp = baseRespawnHp;
        CreatureCmd.SetMaxHp(cs, self, scaledHp);
        CreatureCmd.Heal(cs, self, scaledHp);
    }

    private static void RespawnMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var extra = GetExtra(self);
        extra.Respawns++;
        self.GetPower<AdaptablePower>()?.DoRevive();
        int respawns = extra.Respawns;
        if (respawns == 1)
        {
            Revive(cs, self, SecondFormHp(cs));
            PowerCmd.Apply<PainfulStabsPower>(cs, self, 1, self, null);
        }
        else if (respawns == 2)
        {
            Revive(cs, self, ThirdFormHp(cs));
            PowerCmd.Apply<NemesisPower>(cs, self, 1, self, null);
            PowerCmd.Remove<AdaptablePower>(cs, self);
            PowerCmd.Remove<PainfulStabsPower>(cs, self);
        }
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void SkullBashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SkullBashDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, targets, SkullBashVulnerable, self, null);
    }

    private static void MultiClawMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, MultiClawDamage(cs)).WithHitCount(MultiClawTotalCount(self)).FromMonster(self).Execute();
        GetExtra(self).ExtraMultiClawCount++;
    }

    private static void Phase3LacerateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, Phase3LacerateDamage(cs)).WithHitCount(Phase3LacerateRepeat).FromMonster(self).Execute();
    }

    private static void BigPounceMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BigPounceDamage).FromMonster(self).Execute();
    }

    private static void BurningGrowlMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, BurnCardId, PileType.Discard, BurningGrowlBurnCount(cs), null);
        PowerCmd.Apply<StrengthPower>(cs, self, BurningGrowlStrengthGain(cs), self, null);
    }
}
