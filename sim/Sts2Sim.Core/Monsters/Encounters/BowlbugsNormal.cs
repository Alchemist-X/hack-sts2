namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugsNormal : EncounterDef
{
    public const string EncounterIdEntry = "BOWLBUGS_NORMAL";
    public const int WorkerCount = 2;

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Workers });

    private static readonly string[] SlotNames = { "first", "middle", "last" };

    private static readonly string[] WorkerIds = { "BOWLBUG_EGG", "BOWLBUG_SILK", "BOWLBUG_NECTAR" };

    private static readonly int[] WorkerValidCounts = { 1, 1, 1 };

    public BowlbugsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var currentWorkers = new List<int>();
        var spawns = new List<MonsterSpawn> { new(MonsterRegistry.Get("BOWLBUG_ROCK"), SlotNames[0]) };
        for (int i = 0; i < WorkerCount; i++)
        {
            var items = EncounterPools.ValidIndices(WorkerValidCounts, currentWorkers);
            int picked = rng.NextItem(items);
            currentWorkers.Add(picked);
            spawns.Add(new MonsterSpawn(MonsterRegistry.Get(WorkerIds[picked]), SlotNames[i + 1]));
        }
        return spawns;
    }
}
