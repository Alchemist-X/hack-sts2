namespace Sts2Sim.Core.Run;

public sealed class Overgrowth : ActModel
{
    public const string ActId = "OVERGROWTH";

    public const string GatedAncientId = "NEOW";

    public const string GatedAncientEpochId = "NEOW_EPOCH";

    private static readonly string[] BossDiscoveryOrderIds =
    {
        "VANTOM_BOSS",
        "CEREMONIAL_BEAST_BOSS",
        "THE_KIN_BOSS"
    };

    private static readonly string[] AncientIds =
    {
        "NEOW"
    };

    private static readonly string[] EventIds =
    {
        "AROMA_OF_CHAOS",
        "BYRDONIS_NEST",
        "DENSE_VEGETATION",
        "JUNGLE_MAZE_ADVENTURE",
        "LUMINOUS_CHOIR",
        "MORPHIC_GROVE",
        "SAPPHIRE_SEED",
        "SUNKEN_STATUE",
        "TABLET_OF_TRUTH",
        "UNREST_SITE",
        "WELLSPRING",
        "WHISPERING_HOLLOW",
        "WOOD_CARVINGS"
    };

    private static readonly string[] EncounterIds =
    {
        "BYGONE_EFFIGY_ELITE",
        "BYRDONIS_ELITE",
        "CEREMONIAL_BEAST_BOSS",
        "CUBEX_CONSTRUCT_NORMAL",
        "FLYCONID_NORMAL",
        "FOGMOG_NORMAL",
        "FUZZY_WURM_CRAWLER_WEAK",
        "INKLETS_NORMAL",
        "MAWLER_NORMAL",
        "NIBBITS_NORMAL",
        "NIBBITS_WEAK",
        "OVERGROWTH_CRAWLERS",
        "PHROG_PARASITE_ELITE",
        "RUBY_RAIDERS_NORMAL",
        "SHRINKER_BEETLE_WEAK",
        "SLIMES_NORMAL",
        "SLIMES_WEAK",
        "SLITHERING_STRANGLER_NORMAL",
        "SNAPPING_JAXFRUIT_NORMAL",
        "THE_KIN_BOSS",
        "VANTOM_BOSS",
        "VINE_SHAMBLER_NORMAL"
    };

    private static readonly (string Id, int Index)[] FirstRunNormalEncounters =
    {
        ("NIBBITS_WEAK", 0),
        ("SLIMES_WEAK", 1),
        ("SHRINKER_BEETLE_WEAK", 2),
        ("INKLETS_NORMAL", 3),
        ("MAWLER_NORMAL", 4),
        ("RUBY_RAIDERS_NORMAL", 5),
        ("NIBBITS_NORMAL", 6)
    };

    private static readonly (string Id, int Index)[] FirstRunEvents =
    {
        ("BYRDONIS_NEST", 0),
        ("SAPPHIRE_SEED", 1)
    };

    private static readonly (string Id, int Index)[] FirstRunEliteEncounters =
    {
        ("BYRDONIS_ELITE", 0),
        ("PHROG_PARASITE_ELITE", 1)
    };

    public override string Id => ActId;

    public override int Index => 0;

    public override bool IsDefault => true;

    protected override int NumberOfWeakEncounters => 3;

    protected override int BaseNumberOfRooms => 15;

    public override IEnumerable<EncounterDef> BossDiscoveryOrder => BossDiscoveryOrderIds.Select(EncounterRegistry.Get).ToArray();

    public override IEnumerable<string> AllAncientIds => AncientIds;

    public override IEnumerable<string> AllEventIds => EventIds;

    public override IEnumerable<EncounterDef> GenerateAllEncounters() => EncounterIds.Where(EncounterRegistry.Defs.Contains).Select(EncounterRegistry.Get).ToArray();

    public override IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state)
    {
        var val = AllAncientIds.ToList();
        if (!state.IsEpochRevealed(GatedAncientEpochId))
        {
            val.Remove(GatedAncientId);
        }
        return val;
    }

    protected internal override void ApplyActDiscoveryOrderModifications(ActInstance act, UnlockProfile unlockState)
    {
        if (unlockState.NumberOfRuns == 0)
        {
            foreach (var (id, index) in FirstRunNormalEncounters)
            {
                SwapToOrCreateAtIndex(act.Rooms.normalEncounters, id, index);
            }
            foreach (var (id, index) in FirstRunEvents)
            {
                SwapToOrCreateAtIndex(act.Rooms.events, id, index);
            }
            foreach (var (id, index) in FirstRunEliteEncounters)
            {
                SwapToOrCreateAtIndex(act.Rooms.eliteEncounters, id, index);
            }
        }
    }

    private static void SwapToOrCreateAtIndex(List<EncounterDef> list, string id, int desiredIndex)
    {
        int num = list.FindIndex(e => e.Id == id);
        if (num >= 0)
        {
            var val = list[num];
            var val2 = list[desiredIndex];
            list[desiredIndex] = val;
            list[num] = val2;
        }
        else
        {
            list[desiredIndex] = EncounterRegistry.Get(id);
        }
    }

    private static void SwapToOrCreateAtIndex(List<string> list, string id, int desiredIndex)
    {
        int num = list.FindIndex(e => e == id);
        if (num >= 0)
        {
            var val = list[num];
            var val2 = list[desiredIndex];
            list[desiredIndex] = val;
            list[num] = val2;
        }
        else
        {
            list[desiredIndex] = id;
        }
    }

    public override bool IsUnlocked(UnlockProfile unlockState) => true;

    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
    {
        int restCount = mapRng.NextGaussianInt(7, 1, 6, 7);
        int unknownCount = MapPointTypeCounts.StandardRandomUnknownCount(mapRng);
        return new MapPointTypeCounts(unknownCount, restCount);
    }
}
