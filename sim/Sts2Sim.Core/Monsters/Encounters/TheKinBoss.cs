namespace Sts2Sim.Core.Monsters;

public sealed class TheKinBoss : EncounterDef
{
    public const string EncounterIdEntry = "THE_KIN_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "KIN_FOLLOWER", "KIN_PRIEST" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { "slot1", "slot2", "leaderSlot" });

    public TheKinBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("KIN_FOLLOWER"), "slot1", static m => EncounterMonsterConfig.Set(m, "StartsWithDance", true)),
            new MonsterSpawn(MonsterRegistry.Get("KIN_FOLLOWER"), "slot2"),
            new MonsterSpawn(MonsterRegistry.Get("KIN_PRIEST"), "leaderSlot")
        };
}
