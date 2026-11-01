namespace Sts2Sim.Core.Monsters;

public sealed class GlobeHeadNormal : EncounterDef
{
    public const string EncounterIdEntry = "GLOBE_HEAD_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "GLOBE_HEAD" });

    public GlobeHeadNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("GLOBE_HEAD")) };
}
