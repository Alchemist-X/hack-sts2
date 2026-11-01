namespace Sts2Sim.Core.Monsters;

public sealed class QueenBoss : EncounterDef
{
    public const string EncounterIdEntry = "QUEEN_BOSS";
    private const string QueenSlot = "queen";
    private const string AmalgamSlot = "amalgam";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "QUEEN", "TORCH_HEAD_AMALGAM" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { AmalgamSlot, QueenSlot });

    public QueenBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("TORCH_HEAD_AMALGAM"), AmalgamSlot),
            new MonsterSpawn(MonsterRegistry.Get("QUEEN"), QueenSlot)
        };
}
