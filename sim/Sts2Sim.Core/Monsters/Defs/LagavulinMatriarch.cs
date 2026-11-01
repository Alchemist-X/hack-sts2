namespace Sts2Sim.Core.Monsters;

public sealed class LagavulinMatriarch : MonsterDef
{
    public const string MonsterIdEntry = "LAGAVULIN_MATRIARCH";
    public const string SleepMoveId = "SLEEP_MOVE";
    public const string SlashMoveId = "SLASH_MOVE";
    public const string Slash2MoveId = "SLASH2_MOVE";
    public const string DisembowelMoveId = "DISEMBOWEL_MOVE";
    public const string SoulSiphonMoveId = "SOUL_SIPHON_MOVE";
    public const string SleepBranchId = "SLEEP_BRANCH";

    public const int InitialHpBase = 222;
    public const int InitialHpTough = 233;
    public const int SlashDamageBase = 19;
    public const int SlashDamageDeadly = 21;
    public const int Slash2DamageBase = 12;
    public const int Slash2DamageDeadly = 14;
    public const int Slash2BlockBase = 12;
    public const int Slash2BlockTough = 14;
    public const int DisembowelDamageBase = 9;
    public const int DisembowelDamageDeadly = 10;
    public const int DisembowelRepeat = 2;
    public const int SleepPlating = 12;
    public const int SleepAsleep = 3;
    public const int SoulSiphonStatLoss = -2;
    public const int SoulSiphonStrengthGain = 2;

    public LagavulinMatriarch()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SlashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SlashDamageDeadly, SlashDamageBase);

    public static int Slash2Damage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, Slash2DamageDeadly, Slash2DamageBase);

    public static int Slash2Block(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, Slash2BlockTough, Slash2BlockBase);

    public static int DisembowelDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DisembowelDamageDeadly, DisembowelDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        Sleep(cs, self);
    }

    private static void Sleep(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<PlatingPower>(cs, self, SleepPlating, self, null);
        PowerCmd.Apply<AsleepPower>(cs, self, SleepAsleep, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var sleep = new MoveStateDef(SleepMoveId, SleepMove, Intents.Sleep);
        var slash = new MoveStateDef(SlashMoveId, SlashMove, new SingleAttackIntent(SlashDamage(cs)));
        var slash2 = new MoveStateDef(Slash2MoveId, Slash2Move, new SingleAttackIntent(Slash2Damage(cs)), Intents.Defend);
        var disembowel = new MoveStateDef(DisembowelMoveId, DisembowelMove, new MultiAttackIntent(DisembowelDamage(cs), DisembowelRepeat));
        var soulSiphon = new MoveStateDef(SoulSiphonMoveId, SoulSiphonMove, new DebuffIntent(), Intents.Buff);
        var sleepBranch = new ConditionalBranchStateDef(SleepBranchId);
        sleep.FollowUpState = sleepBranch;
        slash.FollowUpState = disembowel;
        disembowel.FollowUpState = slash2;
        slash2.FollowUpState = soulSiphon;
        soulSiphon.FollowUpState = slash;
        sleepBranch.AddState(sleep, static (_, m) => m.HasPower<AsleepPower>());
        sleepBranch.AddState(slash, static (_, m) => !m.HasPower<AsleepPower>());
        return new MonsterMoveStateMachineDef(new AiStateDef[] { sleepBranch, sleep, slash, slash2, soulSiphon, disembowel }, sleep);
    }

    private static void SleepMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    public static void WakeUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void SlashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SlashDamage(cs)).FromMonster(self).Execute();
    }

    private static void Slash2Move(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, Slash2Damage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, Slash2Block(cs), ValueProp.Move, null);
    }

    private static void DisembowelMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DisembowelDamage(cs)).WithHitCount(DisembowelRepeat).FromMonster(self).Execute();
    }

    private static void SoulSiphonMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, targets, SoulSiphonStatLoss, self, null);
        PowerCmd.Apply<DexterityPower>(cs, targets, SoulSiphonStatLoss, self, null);
        PowerCmd.Apply<StrengthPower>(cs, self, SoulSiphonStrengthGain, self, null);
    }
}
