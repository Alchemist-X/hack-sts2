namespace Sts2Sim.Core.Monsters;

public sealed class SlitheringStranglerNormal : EncounterDef
{
    public const string EncounterIdEntry = "SLITHERING_STRANGLER_NORMAL";

    private enum SecondaryEnemyType
    {
        SnappingJaxfruit,
        MediumSlime,
        SmallSlimes
    }

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Jaxfruit, EncounterTag.Slimes });

    private static readonly IReadOnlyList<string> SmallSlimeIds = Array.AsReadOnly(new[] { LeafSlimeS.MonsterIdEntry, TwigSlimeS.MonsterIdEntry });

    private static readonly IReadOnlyList<string> MediumSlimeIds = Array.AsReadOnly(new[] { LeafSlimeM.MonsterIdEntry, TwigSlimeM.MonsterIdEntry });

    private static readonly IReadOnlyList<SecondaryEnemyType> SecondaryTypes = Array.AsReadOnly(Enum.GetValues<SecondaryEnemyType>());

    public SlitheringStranglerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var ids = new List<string>(3);
        switch (rng.NextItem(SecondaryTypes))
        {
            case SecondaryEnemyType.SnappingJaxfruit:
                ids.Add("SNAPPING_JAXFRUIT");
                break;
            case SecondaryEnemyType.MediumSlime:
                ids.Add(rng.NextItem(MediumSlimeIds)!);
                break;
            case SecondaryEnemyType.SmallSlimes:
                ids.Add(rng.NextItem(SmallSlimeIds)!);
                ids.Add(rng.NextItem(SmallSlimeIds)!);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        ids.Add("SLITHERING_STRANGLER");
        var spawns = new MonsterSpawn[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            spawns[i] = new MonsterSpawn(MonsterRegistry.Get(ids[i]));
        }
        return spawns;
    }
}
