namespace Sts2Sim.Core.Monsters;

public sealed class FabricatorExtraState : MonsterExtraState
{
    public string? LastSpawnedId { get; set; }

    public override MonsterExtraState DeepClone() => new FabricatorExtraState
    {
        LastSpawnedId = LastSpawnedId
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.FabricatorExtra);
        w.WriteBool(LastSpawnedId != null);
        w.WriteString(LastSpawnedId ?? string.Empty);
    }
}

public sealed class Fabricator : MonsterDef
{
    public const string MonsterIdEntry = "FABRICATOR";
    public const string FabricateMoveId = "FABRICATE_MOVE";
    public const string FabricatingStrikeMoveId = "FABRICATING_STRIKE_MOVE";
    public const string DisintegrateMoveId = "DISINTEGRATE_MOVE";
    public const string RandMoveId = "RAND";
    public const string FabricateBranchId = "fabricateBranch";

    public const int HpBase = 150;
    public const int HpTough = 155;
    public const int FabricatingStrikeDamageBase = 18;
    public const int FabricatingStrikeDamageDeadly = 21;
    public const int DisintegrateDamageBase = 11;
    public const int DisintegrateDamageDeadly = 13;
    public const int MaxLivingTeammates = 4;

    public static readonly IReadOnlyList<string> AggroSpawns = new[] { "ZAPBOT", "STABBOT" };

    public static readonly IReadOnlyList<string> DefenseSpawns = new[] { "GUARDBOT", "NOISEBOT" };

    public Fabricator()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int FabricatingStrikeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FabricatingStrikeDamageDeadly, FabricatingStrikeDamageBase);

    public static int DisintegrateDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DisintegrateDamageDeadly, DisintegrateDamageBase);

    public static FabricatorExtraState GetExtra(MonsterState monster)
        => monster.Extra as FabricatorExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Fabricator state");

    public override MonsterExtraState? CreateExtraState() => new FabricatorExtraState();

    public static bool CanFabricate(CombatState cs, MonsterState self)
    {
        var teammates = cs.GetTeammatesOf(self);
        int alive = 0;
        for (int i = 0; i < teammates.Count; i++)
        {
            if (teammates[i].IsAlive)
            {
                alive++;
            }
        }
        return alive < MaxLivingTeammates;
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var fabricate = new MoveStateDef(FabricateMoveId, FabricateMove, Intents.Summon);
        var fabricatingStrike = new MoveStateDef(FabricatingStrikeMoveId, FabricatingStrikeMove, new SingleAttackIntent(FabricatingStrikeDamage(cs)), Intents.Summon);
        var disintegrate = new MoveStateDef(DisintegrateMoveId, DisintegrateMove, new SingleAttackIntent(DisintegrateDamage(cs)));
        var rand = new RandomBranchStateDef(RandMoveId);
        rand.AddBranch(fabricate, MoveRepeatType.CanRepeatForever, 1f);
        rand.AddBranch(fabricatingStrike, MoveRepeatType.CanRepeatForever, 1f);
        var fabricateBranch = new ConditionalBranchStateDef(FabricateBranchId);
        fabricateBranch.AddState(rand, static (c, m) => CanFabricate(c, m));
        fabricateBranch.AddState(disintegrate, static (c, m) => !CanFabricate(c, m));
        fabricate.FollowUpState = fabricateBranch;
        disintegrate.FollowUpState = fabricateBranch;
        fabricatingStrike.FollowUpState = fabricateBranch;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { fabricate, fabricatingStrike, disintegrate, fabricateBranch, rand }, fabricateBranch);
    }

    private static void FabricateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        SpawnBot(cs, self, DefenseSpawns);
        SpawnBot(cs, self, AggroSpawns);
    }

    private static void FabricatingStrikeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FabricatingStrikeDamage(cs)).FromMonster(self).Execute();
        SpawnBot(cs, self, AggroSpawns);
    }

    private static void DisintegrateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DisintegrateDamage(cs)).FromMonster(self).Execute();
    }

    private static void SpawnBot(CombatState cs, MonsterState self, IReadOnlyList<string> options)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        var extra = GetExtra(self);
        var items = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            if (!string.Equals(options[i], extra.LastSpawnedId, StringComparison.Ordinal))
            {
                items.Add(options[i]);
            }
        }
        string picked = cs.Rng.MonsterAi.NextItem((IReadOnlyList<string>)items)!;
        extra.LastSpawnedId = picked;
        string slot = MonsterSpawner.EncounterOf(cs)?.GetNextSlot(cs) ?? throw new InvalidOperationException("Fabricator spawns into its encounter's next slot (Models.Monsters/Fabricator.cs SpawnBot).");
        var bot = CreatureCmd.Add(cs, MonsterRegistry.Get(picked), CombatSide.Enemy, slot);
        PowerCmd.Apply<MinionPower>(cs, bot, 1, self, null);
    }
}
