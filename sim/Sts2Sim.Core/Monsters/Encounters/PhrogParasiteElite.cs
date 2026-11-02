namespace Sts2Sim.Core.Monsters;

public sealed class PhrogParasiteElite : EncounterDef
{
    public const string EncounterIdEntry = "PHROG_PARASITE_ELITE";
    private const string WrigglerSlotPrefix = "wriggler";
    private const string PhrogSlot = "phrog";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "PHROG_PARASITE", "WRIGGLER" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { PhrogSlot, "wriggler1", "wriggler2", "wriggler3", "wriggler4" });

    public PhrogParasiteElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public static string GetWrigglerSlotName(int index) => $"{WrigglerSlotPrefix}{index + 1}";

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("PHROG_PARASITE"), PhrogSlot) };
}
