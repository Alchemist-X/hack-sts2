namespace Sts2Sim.Core.Monsters;

public sealed class DecimillipedeElite : EncounterDef
{
    public const string EncounterIdEntry = "DECIMILLIPEDE_ELITE";
    private const string SegmentSlot = "segment";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "DECIMILLIPEDE_SEGMENT_FRONT", "DECIMILLIPEDE_SEGMENT_MIDDLE", "DECIMILLIPEDE_SEGMENT_BACK" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { SegmentSlot + "1", SegmentSlot + "2", SegmentSlot + "3" });

    public DecimillipedeElite()
        : base(EncounterIdEntry)
    {
    }

    public override IReadOnlyList<string> Slots => SlotNames;

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var front = MonsterRegistry.Get("DECIMILLIPEDE_SEGMENT_FRONT");
        var middle = MonsterRegistry.Get("DECIMILLIPEDE_SEGMENT_MIDDLE");
        var back = MonsterRegistry.Get("DECIMILLIPEDE_SEGMENT_BACK");
        int num = rng.NextInt(3);
        int second = (num + 1) % 3;
        int third = (num + 2) % 3;
        return new[]
        {
            new MonsterSpawn(front, "segment1", m => EncounterMonsterConfig.Set(m, "StarterMoveIdx", num)),
            new MonsterSpawn(middle, "segment2", m => EncounterMonsterConfig.Set(m, "StarterMoveIdx", second)),
            new MonsterSpawn(back, "segment3", m => EncounterMonsterConfig.Set(m, "StarterMoveIdx", third))
        };
    }
}
