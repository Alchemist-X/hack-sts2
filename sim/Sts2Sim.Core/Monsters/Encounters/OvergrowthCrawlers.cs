namespace Sts2Sim.Core.Monsters;

public sealed class OvergrowthCrawlers : EncounterDef
{
    public const string EncounterIdEntry = "OVERGROWTH_CRAWLERS";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Shrinker, EncounterTag.Crawler });

    public OvergrowthCrawlers()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get(ShrinkerBeetle.MonsterIdEntry)),
            new MonsterSpawn(MonsterRegistry.Get(FuzzyWurmCrawler.MonsterIdEntry))
        };
    }
}
