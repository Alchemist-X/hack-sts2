namespace Sts2Sim.Core.Monsters;

public sealed class TunnelerNormal : EncounterDef
{
    public const string EncounterIdEntry = "TUNNELER_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "TUNNELER", "CHOMPER" });
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Burrower, EncounterTag.Chomper });

    public TunnelerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("CHOMPER"), null, static m => EncounterMonsterConfig.Set(m, "ScreamFirst", true)),
            new MonsterSpawn(MonsterRegistry.Get("TUNNELER"))
        };
}
