namespace Sts2Sim.Core.Run;

public sealed class Hive : ActModel
{
    public const string ActId = "HIVE";

    public const string GatedAncientId = "OROBAS";

    public const string GatedAncientEpochId = "OROBAS_EPOCH";

    private static readonly string[] BossDiscoveryOrderIds =
    {
        "THE_INSATIABLE_BOSS",
        "KNOWLEDGE_DEMON_BOSS",
        "KAISER_CRAB_BOSS"
    };

    private static readonly string[] AncientIds =
    {
        "OROBAS",
        "PAEL",
        "TEZCATARA"
    };

    private static readonly string[] EventIds =
    {
        "AMALGAMATOR",
        "BUGSLAYER",
        "COLORFUL_PHILOSOPHERS",
        "COLOSSAL_FLOWER",
        "FIELD_OF_MAN_SIZED_HOLES",
        "INFESTED_AUTOMATON",
        "LOST_WISP",
        "SPIRIT_GRAFTER",
        "THE_LANTERN_KEY",
        "ZEN_WEAVER"
    };

    private static readonly string[] EncounterIds =
    {
        "BOWLBUGS_NORMAL",
        "BOWLBUGS_WEAK",
        "CHOMPERS_NORMAL",
        "DECIMILLIPEDE_ELITE",
        "ENTOMANCER_ELITE",
        "EXOSKELETONS_NORMAL",
        "EXOSKELETONS_WEAK",
        "HUNTER_KILLER_NORMAL",
        "KAISER_CRAB_BOSS",
        "INFESTED_PRISMS_ELITE",
        "KNOWLEDGE_DEMON_BOSS",
        "LOUSE_PROGENITOR_NORMAL",
        "MYTES_NORMAL",
        "OVICOPTER_NORMAL",
        "SLUMBERING_BEETLE_NORMAL",
        "SPINY_TOAD_NORMAL",
        "THE_INSATIABLE_BOSS",
        "THE_OBSCURA_NORMAL",
        "THIEVING_HOPPER_WEAK",
        "TUNNELER_WEAK"
    };

    public override string Id => ActId;

    public override int Index => 1;

    public override bool IsDefault => true;

    protected override int NumberOfWeakEncounters => 2;

    protected override int BaseNumberOfRooms => 14;

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

    public override bool IsUnlocked(UnlockProfile unlockState) => true;

    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
    {
        int restCount = mapRng.NextGaussianInt(6, 1, 6, 7);
        int unknownCount = MapPointTypeCounts.StandardRandomUnknownCount(mapRng) - 1;
        return new MapPointTypeCounts(unknownCount, restCount);
    }
}
