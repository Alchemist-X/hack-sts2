namespace Sts2Sim.Core.Monsters;

public sealed class SlumberingBeetleNormal : EncounterDef
{
    public const string EncounterIdEntry = "SLUMBERING_BEETLE_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Workers });

    public SlumberingBeetleNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("BOWLBUG_ROCK"), "first"),
            new MonsterSpawn(MonsterRegistry.Get("BOWLBUG_SILK"), "second"),
            new MonsterSpawn(MonsterRegistry.Get("SLUMBERING_BEETLE"), "third")
        };
    }
}
