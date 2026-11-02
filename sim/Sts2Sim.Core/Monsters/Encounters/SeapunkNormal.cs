namespace Sts2Sim.Core.Monsters;

public sealed class SeapunkNormal : EncounterDef
{
    public const string EncounterIdEntry = "SEAPUNK_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Seapunk });

    public SeapunkNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("CALCIFIED_CULTIST")),
            new MonsterSpawn(MonsterRegistry.Get(Seapunk.MonsterIdEntry))
        };
    }
}
