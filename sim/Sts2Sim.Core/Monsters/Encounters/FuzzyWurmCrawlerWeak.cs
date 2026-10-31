namespace Sts2Sim.Core.Monsters;

public sealed class FuzzyWurmCrawlerWeak : EncounterDef
{
    public const string EncounterIdEntry = "FUZZY_WURM_CRAWLER_WEAK";

    public FuzzyWurmCrawlerWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(FuzzyWurmCrawler.MonsterIdEntry));
        return new[] { spawn };
    }
}
