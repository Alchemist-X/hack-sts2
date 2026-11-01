namespace Sts2Sim.Core.Monsters;

public sealed class ExoskeletonsNormal : EncounterDef
{
    public const string EncounterIdEntry = "EXOSKELETONS_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Exoskeletons });

    private static readonly IReadOnlyList<string> SlotList = Array.AsReadOnly(new[] { "first", "second", "third", "fourth" });

    public ExoskeletonsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<string> Slots => SlotList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("EXOSKELETON"), "first"),
            new MonsterSpawn(MonsterRegistry.Get("EXOSKELETON"), "second"),
            new MonsterSpawn(MonsterRegistry.Get("EXOSKELETON"), "third"),
            new MonsterSpawn(MonsterRegistry.Get("EXOSKELETON"), "fourth")
        };
    }
}
