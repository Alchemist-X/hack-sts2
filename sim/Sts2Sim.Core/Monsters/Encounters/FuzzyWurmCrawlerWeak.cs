namespace Sts2Sim.Core.Monsters;

public sealed class FuzzyWurmCrawlerWeak : EncounterDef
{
    public const string EncounterIdEntry = "FUZZY_WURM_CRAWLER_WEAK";

    public FuzzyWurmCrawlerWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    // Models.Encounters/FuzzyWurmCrawlerWeak.cs `Tags => [EncounterTag.Crawler]` (E3: ActModel.AddWithoutRepeatingTags, Models/ActModel.cs:357-369)
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Crawler });

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(FuzzyWurmCrawler.MonsterIdEntry));
        return new[] { spawn };
    }
}
