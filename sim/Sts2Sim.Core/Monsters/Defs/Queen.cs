namespace Sts2Sim.Core.Monsters;

public sealed class QueenExtraState : MonsterExtraState
{
    public bool HasAmalgamDied { get; set; }

    public int? AmalgamId { get; set; }

    public override MonsterExtraState DeepClone() => new QueenExtraState
    {
        HasAmalgamDied = HasAmalgamDied,
        AmalgamId = AmalgamId
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.QueenExtra);
        w.WriteBool(HasAmalgamDied);
        w.WriteBool(AmalgamId.HasValue);
        w.WriteInt32(AmalgamId ?? 0);
    }
}

public sealed class Queen : MonsterDef
{
    public const string MonsterIdEntry = "QUEEN";
    public const string TorchHeadAmalgamMonsterId = "TORCH_HEAD_AMALGAM";
    public const string PuppetStringsMoveId = "PUPPET_STRINGS_MOVE";
    public const string YouAreMineMoveId = "YOU_ARE_MINE_MOVE";
    public const string YoureMineNowBranchId = "YOURE_MINE_NOW_BRANCH";
    public const string BurnBrightForMeMoveId = "BURN_BRIGHT_FOR_ME_MOVE";
    public const string BurnBrightForMeBranchId = "BURN_BRIGHT_FOR_ME_BRANCH";
    public const string OffWithYourHeadMoveId = "OFF_WITH_YOUR_HEAD_MOVE";
    public const string ExecutionMoveId = "EXECUTION_MOVE";
    public const string EnrageMoveId = "ENRAGE_MOVE";

    public const int HpBase = 400;
    public const int HpTough = 419;
    public const int OffWithYourHeadDamageBase = 3;
    public const int OffWithYourHeadDamageDeadly = 4;
    public const int OffWithYourHeadRepeat = 5;
    public const int ExecutionDamageBase = 15;
    public const int ExecutionDamageDeadly = 18;
    public const int ChainsOfBindingAmount = 3;
    public const int YoureMineAmount = 99;
    public const int BurnBrightStrengthBase = 1;
    public const int BurnBrightStrengthDeadly = 1;
    public const int BurnBrightBlock = 20;
    public const int EnrageStrength = 2;

    public Queen()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int OffWithYourHeadDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, OffWithYourHeadDamageDeadly, OffWithYourHeadDamageBase);

    public static int ExecutionDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ExecutionDamageDeadly, ExecutionDamageBase);

    public static QueenExtraState GetExtra(MonsterState monster)
        => monster.Extra as QueenExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Queen state");

    public override MonsterExtraState? CreateExtraState() => new QueenExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (string.Equals(enemies[i].Def.Id, TorchHeadAmalgamMonsterId, StringComparison.Ordinal))
            {
                GetExtra(self).AmalgamId = enemies[i].CombatId;
                return;
            }
        }
        throw new InvalidOperationException("Sequence contains no matching element");
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var puppetStrings = new MoveStateDef(PuppetStringsMoveId, PuppetStringsMove, Intents.CardDebuff);
        var youAreMine = new MoveStateDef(YouAreMineMoveId, YoureMineMove, new DebuffIntent());
        var youreMineBranch = new ConditionalBranchStateDef(YoureMineNowBranchId);
        var burnBright = new MoveStateDef(BurnBrightForMeMoveId, BurnBrightForMeMove, Intents.Buff, Intents.Defend);
        var burnBrightBranch = new ConditionalBranchStateDef(BurnBrightForMeBranchId);
        var offWithYourHead = new MoveStateDef(OffWithYourHeadMoveId, OffWithYourHeadMove, new MultiAttackIntent(OffWithYourHeadDamage(cs), OffWithYourHeadRepeat));
        var execution = new MoveStateDef(ExecutionMoveId, ExecutionMove, new SingleAttackIntent(ExecutionDamage(cs)));
        var enraged = new MoveStateDef(EnrageMoveId, EnrageMove, Intents.Buff);
        puppetStrings.FollowUpState = youAreMine;
        youAreMine.FollowUpState = youreMineBranch;
        youreMineBranch.AddState(burnBright, static (_, m) => !GetExtra(m).HasAmalgamDied);
        youreMineBranch.AddState(offWithYourHead, static (_, m) => GetExtra(m).HasAmalgamDied);
        burnBright.FollowUpState = burnBrightBranch;
        burnBrightBranch.AddState(burnBright, static (_, m) => !GetExtra(m).HasAmalgamDied);
        burnBrightBranch.AddState(offWithYourHead, static (_, m) => GetExtra(m).HasAmalgamDied);
        offWithYourHead.FollowUpState = execution;
        execution.FollowUpState = enraged;
        enraged.FollowUpState = offWithYourHead;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { puppetStrings, youAreMine, burnBright, burnBrightBranch, youreMineBranch, offWithYourHead, execution, enraged }, puppetStrings);
    }

    public override void AfterDeath(RunState rs, MonsterState self, CreatureState creature, bool wasRemovalPrevented)
    {
        if (creature is MonsterState dead && string.Equals(dead.Def.Id, TorchHeadAmalgamMonsterId, StringComparison.Ordinal) && self.IsAlive)
        {
            var extra = GetExtra(self);
            extra.HasAmalgamDied = true;
            extra.AmalgamId = null;
            if (self.HasAi && self.Ai.NextMove != null && string.Equals(self.Ai.NextMove.Id, BurnBrightForMeMoveId, StringComparison.Ordinal)
                && self.Ai.Machine.TryGet(EnrageMoveId, out var state) && state is MoveStateDef enraged)
            {
                MonsterLifecycle.SetMoveImmediate(self, enraged);
            }
        }
    }

    private static void PuppetStringsMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<ChainsOfBindingPower>(cs, targets, ChainsOfBindingAmount, self, null);
    }

    private static void YoureMineMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<FrailPower>(cs, targets, YoureMineAmount, self, null);
        PowerCmd.Apply<WeakPower>(cs, targets, YoureMineAmount, self, null);
        PowerCmd.Apply<VulnerablePower>(cs, targets, YoureMineAmount, self, null);
    }

    private static void BurnBrightForMeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        int strengthAmount = cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BurnBrightStrengthDeadly, BurnBrightStrengthBase);
        var teammates = new List<CreatureState>(cs.GetTeammatesOf(self));
        for (int i = 0; i < teammates.Count; i++)
        {
            if (!ReferenceEquals(teammates[i], self))
            {
                PowerCmd.Apply<StrengthPower>(cs, teammates[i], strengthAmount, self, null);
            }
        }
        CreatureCmd.GainBlock(cs, self, BurnBrightBlock, ValueProp.Move, null);
    }

    private static void OffWithYourHeadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, OffWithYourHeadDamage(cs)).WithHitCount(OffWithYourHeadRepeat).FromMonster(self).Execute();
    }

    private static void ExecutionMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ExecutionDamage(cs)).FromMonster(self).Execute();
    }

    private static void EnrageMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, EnrageStrength, self, null);
    }
}
