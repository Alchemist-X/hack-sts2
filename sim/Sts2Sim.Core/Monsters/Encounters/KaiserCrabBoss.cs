namespace Sts2Sim.Core.Monsters;

public sealed class KaiserCrabBoss : EncounterDef
{
    public const string EncounterIdEntry = "KAISER_CRAB_BOSS";

    private static readonly IReadOnlyList<string> SlotList = Array.AsReadOnly(new[] { "crusher", "rocket" });

    public KaiserCrabBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> Slots => SlotList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("CRUSHER"), "crusher"),
            new MonsterSpawn(MonsterRegistry.Get("ROCKET"), "rocket")
        };
    }
}
