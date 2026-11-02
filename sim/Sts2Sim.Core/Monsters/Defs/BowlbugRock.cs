namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugRockExtraState : MonsterExtraState
{
    public bool IsOffBalance { get; set; }

    public override MonsterExtraState DeepClone() => new BowlbugRockExtraState
    {
        IsOffBalance = IsOffBalance
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.BowlbugRockExtra);
        w.WriteBool(IsOffBalance);
    }
}

public sealed class BowlbugRock : MonsterDef
{
    public const string MonsterIdEntry = "BOWLBUG_ROCK";
    public const string HeadbuttMoveId = "HEADBUTT_MOVE";
    public const string DizzyMoveId = "DIZZY_MOVE";
    public const string PostHeadbuttStateId = "POST_HEADBUTT";

    public const int MinInitialHpBase = 45;
    public const int MinInitialHpTough = 46;
    public const int MaxInitialHpBase = 48;
    public const int MaxInitialHpTough = 49;
    public const int HeadbuttDamageBase = 15;
    public const int HeadbuttDamageDeadly = 16;
    public const int ImbalancedAmount = 1;

    public BowlbugRock()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int HeadbuttDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HeadbuttDamageDeadly, HeadbuttDamageBase);

    public static BowlbugRockExtraState GetExtra(MonsterState monster)
        => monster.Extra as BowlbugRockExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no BowlbugRock state");

    public override MonsterExtraState? CreateExtraState() => new BowlbugRockExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<ImbalancedPower>(cs, self, ImbalancedAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var headbutt = new MoveStateDef(HeadbuttMoveId, HeadbuttMove, new SingleAttackIntent(HeadbuttDamage(cs)));
        var dizzy = new MoveStateDef(DizzyMoveId, DizzyMove, Intents.Stun);
        var postHeadbutt = new ConditionalBranchStateDef(PostHeadbuttStateId);
        headbutt.FollowUpState = postHeadbutt;
        dizzy.FollowUpState = headbutt;
        postHeadbutt.AddState(dizzy, static (_, m) => GetExtra(m).IsOffBalance);
        postHeadbutt.AddState(headbutt, static (_, m) => !GetExtra(m).IsOffBalance);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { dizzy, postHeadbutt, headbutt }, headbutt);
    }

    private static void HeadbuttMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HeadbuttDamage(cs)).FromMonster(self).Execute();
        if (GetExtra(self).IsOffBalance)
        {
            Stun(cs, self);
        }
    }

    private static void Stun(CombatState cs, MonsterState self)
    {
        CreatureCmd.StunWithMove(cs, self, DizzyMove);
    }

    private static void DizzyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsOffBalance = false;
    }
}
