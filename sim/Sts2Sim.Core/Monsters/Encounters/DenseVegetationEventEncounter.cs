namespace Sts2Sim.Core.Monsters;

public sealed class DenseVegetationEventEncounter : EncounterDef
{
    public const string EncounterIdEntry = "DENSE_VEGETATION_EVENT_ENCOUNTER";
    private const string WrigglerSlotPrefix = "wriggler";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "WRIGGLER" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { WrigglerSlotPrefix + "1", WrigglerSlotPrefix + "2", WrigglerSlotPrefix + "3", WrigglerSlotPrefix + "4" });

    public DenseVegetationEventEncounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawns = new List<MonsterSpawn>(SlotNames.Count);
        foreach (var slot in Slots)
        {
            spawns.Add(new MonsterSpawn(MonsterRegistry.Get("WRIGGLER"), slot, static m => EncounterMonsterConfig.Set(m, "StartStunned", false)));
        }
        return spawns;
    }
}
