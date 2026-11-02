namespace Sts2Sim.Core.Monsters;

public sealed class SnappingJaxfruitNormal : EncounterDef
{
    public const string EncounterIdEntry = "SNAPPING_JAXFRUIT_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Mushroom, EncounterTag.Jaxfruit });

    public SnappingJaxfruitNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("SNAPPING_JAXFRUIT")),
            new MonsterSpawn(MonsterRegistry.Get("FLYCONID"))
        };
    }
}
