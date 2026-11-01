namespace Sts2Sim.Core.Run;

public sealed class Glory : ActModel
{
    public const string ActId = "GLORY";

    private static readonly string[] BossDiscoveryOrderIds =
    {
        "QUEEN_BOSS",
        "TEST_SUBJECT_BOSS",
        "AEONGLASS_BOSS"
    };

    private static readonly string[] AncientIds =
    {
        "NONUPEIPE",
        "TANX",
        "VAKUU"
    };

    private static readonly string[] EventIds =
    {
        "BATTLEWORN_DUMMY",
        "GRAVE_OF_THE_FORGOTTEN",
        "HUNGRY_FOR_MUSHROOMS",
        "REFLECTIONS",
        "ROUND_TEA_PARTY",
        "TRIAL",
        "TINKER_TIME"
    };

    private static readonly string[] EncounterIds =
    {
        "AXEBOTS_NORMAL",
        "CONSTRUCT_MENAGERIE_NORMAL",
        "DEVOTED_SCULPTOR_WEAK",
        "AEONGLASS_BOSS",
        "FABRICATOR_NORMAL",
        "FROG_KNIGHT_NORMAL",
        "GLOBE_HEAD_NORMAL",
        "KNIGHTS_ELITE",
        "MECHA_KNIGHT_ELITE",
        "OWL_MAGISTRATE_NORMAL",
        "QUEEN_BOSS",
        "SCROLLS_OF_BITING_NORMAL",
        "SCROLLS_OF_BITING_WEAK",
        "SLIMED_BERSERKER_NORMAL",
        "SOUL_NEXUS_ELITE",
        "TEST_SUBJECT_BOSS",
        "THE_LOST_AND_FORGOTTEN_NORMAL",
        "TURRET_OPERATOR_WEAK"
    };

    public override string Id => ActId;

    public override int Index => 2;

    public override bool IsDefault => true;

    protected override int NumberOfWeakEncounters => 2;

    protected override int BaseNumberOfRooms => 13;

    public override IEnumerable<EncounterDef> BossDiscoveryOrder => BossDiscoveryOrderIds.Select(EncounterRegistry.Get).ToArray();

    public override IEnumerable<string> AllAncientIds => AncientIds;

    public override IEnumerable<string> AllEventIds => EventIds;

    public override IEnumerable<EncounterDef> GenerateAllEncounters() => EncounterIds.Where(EncounterRegistry.Defs.Contains).Select(EncounterRegistry.Get).ToArray();

    public override IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state) => AllAncientIds.ToList();

    public override bool IsUnlocked(UnlockProfile unlockState) => true;

    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
    {
        int restCount = mapRng.NextInt(5, 7);
        int unknownCount = MapPointTypeCounts.StandardRandomUnknownCount(mapRng) - 1;
        return new MapPointTypeCounts(unknownCount, restCount);
    }
}
