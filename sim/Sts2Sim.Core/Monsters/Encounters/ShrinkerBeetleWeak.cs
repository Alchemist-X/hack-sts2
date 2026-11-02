namespace Sts2Sim.Core.Monsters;

public sealed class ShrinkerBeetleWeak : EncounterDef
{
    public const string EncounterIdEntry = "SHRINKER_BEETLE_WEAK";

    public ShrinkerBeetleWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    // Models.Encounters/ShrinkerBeetleWeak.cs `Tags => [EncounterTag.Shrinker]` (E3: ActModel.AddWithoutRepeatingTags, Models/ActModel.cs:357-369)
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Shrinker });

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(ShrinkerBeetle.MonsterIdEntry));
        return new[] { spawn };
    }
}
