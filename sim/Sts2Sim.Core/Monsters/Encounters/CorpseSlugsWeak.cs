namespace Sts2Sim.Core.Monsters;

public sealed class CorpseSlugsWeak : EncounterDef
{
    public const string EncounterIdEntry = "CORPSE_SLUGS_WEAK";
    public const int SlugCount = 2;

    public CorpseSlugsWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    // Models.Encounters/CorpseSlugsWeak.cs `Tags => [EncounterTag.Slugs]` (E3: ActModel.AddWithoutRepeatingTags, Models/ActModel.cs:357-369)
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Slugs });

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var slug = MonsterRegistry.Get(CorpseSlug.MonsterIdEntry);
        var spawns = new MonsterSpawn[SlugCount];
        for (int i = 0; i < spawns.Length; i++)
        {
            spawns[i] = new MonsterSpawn(slug);
        }
        return CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(spawns, rng);
    }
}
