namespace Sts2Sim.Core.Monsters;

public sealed class MytesNormal : EncounterDef
{
    public const string EncounterIdEntry = "MYTES_NORMAL";
    public const string FirstSlot = "first";
    public const string SecondSlot = "second";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "MYTE" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { FirstSlot, SecondSlot });

    public MytesNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("MYTE"), FirstSlot),
            new MonsterSpawn(MonsterRegistry.Get("MYTE"), SecondSlot)
        };
}
