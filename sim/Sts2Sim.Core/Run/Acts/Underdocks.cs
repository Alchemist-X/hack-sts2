namespace Sts2Sim.Core.Run;

public sealed class Underdocks : ActModel
{
    public const string ActId = "UNDERDOCKS";

    public const string GatedAncientId = "NEOW";

    public const string GatedAncientEpochId = "NEOW_EPOCH";

    public const string UnderdocksEpochId = "UNDERDOCKS_EPOCH";

    private static readonly string[] BossDiscoveryOrderIds =
    {
        "WATERFALL_GIANT_BOSS",
        "SOUL_FYSH_BOSS",
        "LAGAVULIN_MATRIARCH_BOSS"
    };

    private static readonly string[] AncientIds =
    {
        "NEOW"
    };

    private static readonly string[] EventIds =
    {
        "ABYSSAL_BATHS",
        "DROWNING_BEACON",
        "ENDLESS_CONVEYOR",
        "PUNCH_OFF",
        "SPIRALING_WHIRLPOOL",
        "SUNKEN_STATUE",
        "SUNKEN_TREASURY",
        "DOORS_OF_LIGHT_AND_DARK",
        "TRASH_HEAP",
        "WATERLOGGED_SCRIPTORIUM"
    };

    private static readonly string[] EncounterIds =
    {
        "CORPSE_SLUGS_NORMAL",
        "CORPSE_SLUGS_WEAK",
        "CULTISTS_NORMAL",
        "FOSSIL_STALKER_NORMAL",
        "GREMLIN_MERC_NORMAL",
        "HAUNTED_SHIP_NORMAL",
        "LAGAVULIN_MATRIARCH_BOSS",
        "LIVING_FOG_NORMAL",
        "PHANTASMAL_GARDENERS_ELITE",
        "PUNCH_CONSTRUCT_NORMAL",
        "SEAPUNK_NORMAL",
        "SEAPUNK_WEAK",
        "SEWER_CLAM_NORMAL",
        "SKULKING_COLONY_ELITE",
        "SLUDGE_SPINNER_WEAK",
        "SOUL_FYSH_BOSS",
        "TERROR_EEL_ELITE",
        "TOADPOLES_WEAK",
        "TWO_TAILED_RATS_NORMAL",
        "WATERFALL_GIANT_BOSS"
    };

    public override string Id => ActId;

    public override int Index => 0;

    public override bool IsDefault => false;

    protected override int NumberOfWeakEncounters => 3;

    protected override int BaseNumberOfRooms => 15;

    public override IEnumerable<EncounterDef> BossDiscoveryOrder => BossDiscoveryOrderIds.Select(EncounterRegistry.Get).ToArray();

    public override IEnumerable<string> AllAncientIds => AncientIds;

    public override IEnumerable<string> AllEventIds => EventIds;

    public override IEnumerable<EncounterDef> GenerateAllEncounters() => EncounterIds.Where(EncounterRegistry.Defs.Contains).Select(EncounterRegistry.Get).ToArray();

    public override IReadOnlyList<string> DeclaredEncounterIds => EncounterIds;

    public override IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state)
    {
        var val = AllAncientIds.ToList();
        if (!state.IsEpochRevealed(GatedAncientEpochId))
        {
            val.Remove(GatedAncientId);
        }
        return val;
    }

    public override bool IsUnlocked(UnlockProfile unlockState) => unlockState.IsEpochRevealed(UnderdocksEpochId);

    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
    {
        int restCount = mapRng.NextGaussianInt(7, 1, 6, 7);
        int unknownCount = MapPointTypeCounts.StandardRandomUnknownCount(mapRng);
        return new MapPointTypeCounts(unknownCount, restCount);
    }
}
