namespace Sts2Sim.Core.Monsters;

public sealed class CorpseSlugsNormal : EncounterDef
{
    public const string EncounterIdEntry = "CORPSE_SLUGS_NORMAL";
    public const int SlugCount = 3;

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Slugs });

    public CorpseSlugsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override RoomType RoomType => RoomType.Monster;

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
