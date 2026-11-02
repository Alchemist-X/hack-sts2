namespace Sts2Sim.Core.Monsters;

public sealed class SeapunkWeak : EncounterDef
{
    public const string EncounterIdEntry = "SEAPUNK_WEAK";

    public SeapunkWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    // Models.Encounters/SeapunkWeak.cs `Tags => [EncounterTag.Seapunk]` (E3: ActModel.AddWithoutRepeatingTags, Models/ActModel.cs:357-369)
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Seapunk });

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[] { new MonsterSpawn(MonsterRegistry.Get(Seapunk.MonsterIdEntry)) };
    }
}
