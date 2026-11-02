namespace Sts2Sim.Core.Monsters;

public sealed class KnightsElite : EncounterDef
{
    public const string EncounterIdEntry = "KNIGHTS_ELITE";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "FLAIL_KNIGHT", "SPECTRAL_KNIGHT", "MAGI_KNIGHT" });
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Knights });

    public KnightsElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("FLAIL_KNIGHT"), "first"),
            new MonsterSpawn(MonsterRegistry.Get("SPECTRAL_KNIGHT"), "second"),
            new MonsterSpawn(MonsterRegistry.Get("MAGI_KNIGHT"), "third")
        };
}
