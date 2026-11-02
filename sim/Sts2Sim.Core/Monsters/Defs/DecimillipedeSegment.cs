namespace Sts2Sim.Core.Monsters;

public sealed class DecimillipedeSegmentExtraState : MonsterExtraState
{
    public int StarterMoveIdx { get; set; }

    public override MonsterExtraState DeepClone() => new DecimillipedeSegmentExtraState
    {
        StarterMoveIdx = StarterMoveIdx
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.DecimillipedeSegmentExtra);
        w.WriteInt32(StarterMoveIdx);
    }
}

public abstract class DecimillipedeSegment : MonsterDef
{
    public const string BaseMonsterIdEntry = "DECIMILLIPEDE_SEGMENT";
    public const string WritheMoveId = "WRITHE_MOVE";
    public const string BulkMoveId = "BULK_MOVE";
    public const string ConstrictMoveId = "CONSTRICT_MOVE";
    public const string DeadMoveId = "DEAD_MOVE";
    public const string ReattachMoveId = "REATTACH_MOVE";
    public const string RandMoveId = "RAND";

    public const int WritheRepeat = 2;
    public const int MinInitialHpBase = 40;
    public const int MinInitialHpTough = 46;
    public const int MaxInitialHpBase = 46;
    public const int MaxInitialHpTough = 52;
    public const int WritheDamageBase = 5;
    public const int WritheDamageDeadly = 6;
    public const int ConstrictDamageBase = 8;
    public const int ConstrictDamageDeadly = 9;
    public const int BulkDamageBase = 6;
    public const int BulkDamageDeadly = 7;
    public const int BulkStrength = 2;
    public const int ConstrictWeak = 1;
    public const int ReattachAmount = 25;
    public const int StarterMoveCount = 3;

    protected DecimillipedeSegment(string id)
        : base(id)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int WritheDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WritheDamageDeadly, WritheDamageBase);

    public static int ConstrictDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ConstrictDamageDeadly, ConstrictDamageBase);

    public static int BulkDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BulkDamageDeadly, BulkDamageBase);

    public static DecimillipedeSegmentExtraState GetExtra(MonsterState monster)
        => monster.Extra as DecimillipedeSegmentExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no DecimillipedeSegment state");

    public override MonsterExtraState? CreateExtraState() => new DecimillipedeSegmentExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        decimal maxHp = self.MaxHp;
        if (maxHp % 2m == 1m)
        {
            maxHp += 1m;
        }
        var others = new List<CreatureState>();
        var teammates = cs.GetTeammatesOf(self);
        for (int i = 0; i < teammates.Count; i++)
        {
            if (!ReferenceEquals(teammates[i], self))
            {
                others.Add(teammates[i]);
            }
        }
        while (AnyHasMaxHp(others, maxHp))
        {
            maxHp += 2m;
            if (maxHp > MaxInitialHp(cs))
            {
                maxHp = MinInitialHp(cs);
            }
        }
        CreatureCmd.SetMaxAndCurrentHp(cs, self, maxHp);
        PowerCmd.Apply<ReattachPower>(cs, self, ReattachAmount, self, null);
    }

    private static bool AnyHasMaxHp(List<CreatureState> creatures, decimal maxHp)
    {
        for (int i = 0; i < creatures.Count; i++)
        {
            if (creatures[i].MaxHp == maxHp)
            {
                return true;
            }
        }
        return false;
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var writhe = new MoveStateDef(WritheMoveId, WritheMove, new MultiAttackIntent(WritheDamage(cs), WritheRepeat));
        var bulk = new MoveStateDef(BulkMoveId, BulkMove, new SingleAttackIntent(BulkDamage(cs)), Intents.Buff);
        var constrict = new MoveStateDef(ConstrictMoveId, ConstrictMove, new SingleAttackIntent(ConstrictDamage(cs)), new DebuffIntent());
        var dead = new MoveStateDef(DeadMoveId, DeadMove);
        var reattach = new MoveStateDef(ReattachMoveId, ReattachMove, Intents.Heal)
        {
            MustPerformOnceBeforeTransitioning = true
        };
        constrict.FollowUpState = bulk;
        bulk.FollowUpState = writhe;
        writhe.FollowUpState = constrict;
        var rand = new RandomBranchStateDef(RandMoveId);
        dead.FollowUpState = reattach;
        reattach.FollowUpState = rand;
        rand.AddBranch(writhe, MoveRepeatType.CannotRepeat);
        rand.AddBranch(bulk, MoveRepeatType.CannotRepeat);
        rand.AddBranch(constrict, MoveRepeatType.CannotRepeat);
        AiStateDef initial = (GetExtra(self).StarterMoveIdx % StarterMoveCount) switch
        {
            0 => writhe,
            1 => bulk,
            _ => constrict
        };
        return new MonsterMoveStateMachineDef(new AiStateDef[] { writhe, bulk, constrict, dead, reattach, rand }, initial);
    }

    private static void WritheMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WritheDamage(cs)).WithHitCount(WritheRepeat).FromMonster(self).Execute();
    }

    private static void BulkMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BulkDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, BulkStrength, self, null);
    }

    private static void ConstrictMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ConstrictDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, ConstrictWeak, self, null);
    }

    private static void DeadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private static void ReattachMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        self.GetPower<ReattachPower>()!.DoReattach(cs);
    }
}
