namespace Sts2Sim.Core.Run;

public static class EncounterRoomTypes
{
    private static readonly Dictionary<string, RoomType> RoomTypes = new(StringComparer.Ordinal)
    {
        ["AEONGLASS_BOSS"] = RoomType.Boss,
        ["AXEBOTS_NORMAL"] = RoomType.Monster,
        ["BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER"] = RoomType.Monster,
        ["BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER"] = RoomType.Monster,
        ["BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER"] = RoomType.Monster,
        ["BOWLBUGS_NORMAL"] = RoomType.Monster,
        ["BOWLBUGS_WEAK"] = RoomType.Monster,
        ["BYGONE_EFFIGY_ELITE"] = RoomType.Elite,
        ["BYRDONIS_ELITE"] = RoomType.Elite,
        ["CEREMONIAL_BEAST_BOSS"] = RoomType.Boss,
        ["CHOMPERS_NORMAL"] = RoomType.Monster,
        ["CONSTRUCT_MENAGERIE_NORMAL"] = RoomType.Monster,
        ["CORPSE_SLUGS_NORMAL"] = RoomType.Monster,
        ["CORPSE_SLUGS_WEAK"] = RoomType.Monster,
        ["CUBEX_CONSTRUCT_NORMAL"] = RoomType.Monster,
        ["CULTISTS_NORMAL"] = RoomType.Monster,
        ["DECIMILLIPEDE_ELITE"] = RoomType.Elite,
        ["DENSE_VEGETATION_EVENT_ENCOUNTER"] = RoomType.Monster,
        ["DEPRECATED_ENCOUNTER"] = RoomType.Monster,
        ["DEVOTED_SCULPTOR_WEAK"] = RoomType.Monster,
        ["ENTOMANCER_ELITE"] = RoomType.Elite,
        ["EXOSKELETONS_NORMAL"] = RoomType.Monster,
        ["EXOSKELETONS_WEAK"] = RoomType.Monster,
        ["FABRICATOR_NORMAL"] = RoomType.Monster,
        ["FAKE_MERCHANT_EVENT_ENCOUNTER"] = RoomType.Monster,
        ["FLYCONID_NORMAL"] = RoomType.Monster,
        ["FOGMOG_NORMAL"] = RoomType.Monster,
        ["FOSSIL_STALKER_NORMAL"] = RoomType.Monster,
        ["FROG_KNIGHT_NORMAL"] = RoomType.Monster,
        ["FUZZY_WURM_CRAWLER_WEAK"] = RoomType.Monster,
        ["GLOBE_HEAD_NORMAL"] = RoomType.Monster,
        ["GREMLIN_MERC_NORMAL"] = RoomType.Monster,
        ["HAUNTED_SHIP_NORMAL"] = RoomType.Monster,
        ["HUNTER_KILLER_NORMAL"] = RoomType.Monster,
        ["INFESTED_PRISMS_ELITE"] = RoomType.Elite,
        ["INKLETS_NORMAL"] = RoomType.Monster,
        ["KAISER_CRAB_BOSS"] = RoomType.Boss,
        ["KNIGHTS_ELITE"] = RoomType.Elite,
        ["KNOWLEDGE_DEMON_BOSS"] = RoomType.Boss,
        ["LAGAVULIN_MATRIARCH_BOSS"] = RoomType.Boss,
        ["LIVING_FOG_NORMAL"] = RoomType.Monster,
        ["LOUSE_PROGENITOR_NORMAL"] = RoomType.Monster,
        ["MAWLER_NORMAL"] = RoomType.Monster,
        ["MECHA_KNIGHT_ELITE"] = RoomType.Elite,
        ["MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER"] = RoomType.Monster,
        ["MYTES_NORMAL"] = RoomType.Monster,
        ["NIBBITS_NORMAL"] = RoomType.Monster,
        ["NIBBITS_WEAK"] = RoomType.Monster,
        ["OVERGROWTH_CRAWLERS"] = RoomType.Monster,
        ["OVICOPTER_NORMAL"] = RoomType.Monster,
        ["OWL_MAGISTRATE_NORMAL"] = RoomType.Monster,
        ["PHANTASMAL_GARDENERS_ELITE"] = RoomType.Elite,
        ["PHROG_PARASITE_ELITE"] = RoomType.Elite,
        ["PUNCH_CONSTRUCT_NORMAL"] = RoomType.Monster,
        ["PUNCH_OFF_EVENT_ENCOUNTER"] = RoomType.Monster,
        ["QUEEN_BOSS"] = RoomType.Boss,
        ["RUBY_RAIDERS_NORMAL"] = RoomType.Monster,
        ["SCROLLS_OF_BITING_NORMAL"] = RoomType.Monster,
        ["SCROLLS_OF_BITING_WEAK"] = RoomType.Monster,
        ["SEAPUNK_NORMAL"] = RoomType.Monster,
        ["SEAPUNK_WEAK"] = RoomType.Monster,
        ["SEWER_CLAM_NORMAL"] = RoomType.Monster,
        ["SHRINKER_BEETLE_WEAK"] = RoomType.Monster,
        ["SKULKING_COLONY_ELITE"] = RoomType.Elite,
        ["SLIMED_BERSERKER_NORMAL"] = RoomType.Monster,
        ["SLIMES_NORMAL"] = RoomType.Monster,
        ["SLIMES_WEAK"] = RoomType.Monster,
        ["SLITHERING_STRANGLER_NORMAL"] = RoomType.Monster,
        ["SLUDGE_SPINNER_WEAK"] = RoomType.Monster,
        ["SLUMBERING_BEETLE_NORMAL"] = RoomType.Monster,
        ["SNAPPING_JAXFRUIT_NORMAL"] = RoomType.Monster,
        ["SOUL_FYSH_BOSS"] = RoomType.Boss,
        ["SOUL_NEXUS_ELITE"] = RoomType.Elite,
        ["SPINY_TOAD_NORMAL"] = RoomType.Monster,
        ["TERROR_EEL_ELITE"] = RoomType.Elite,
        ["TEST_SUBJECT_BOSS"] = RoomType.Boss,
        ["THE_ARCHITECT_EVENT_ENCOUNTER"] = RoomType.Monster,
        ["THE_INSATIABLE_BOSS"] = RoomType.Boss,
        ["THE_KIN_BOSS"] = RoomType.Boss,
        ["THE_LOST_AND_FORGOTTEN_NORMAL"] = RoomType.Monster,
        ["THE_OBSCURA_NORMAL"] = RoomType.Monster,
        ["THIEVING_HOPPER_WEAK"] = RoomType.Monster,
        ["TOADPOLES_WEAK"] = RoomType.Monster,
        ["TUNNELER_NORMAL"] = RoomType.Monster,
        ["TUNNELER_WEAK"] = RoomType.Monster,
        ["TURRET_OPERATOR_WEAK"] = RoomType.Monster,
        ["TWO_TAILED_RATS_NORMAL"] = RoomType.Monster,
        ["VANTOM_BOSS"] = RoomType.Boss,
        ["VINE_SHAMBLER_NORMAL"] = RoomType.Monster,
        ["WATERFALL_GIANT_BOSS"] = RoomType.Boss,
    };

    private static readonly HashSet<string> NoRewards = new(StringComparer.Ordinal)
    {
        "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER",
        "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER",
        "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER",
    };

    public static int Count => RoomTypes.Count;

    public static RoomType Of(string encounterId)
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        return RoomTypes.TryGetValue(encounterId, out var roomType)
            ? roomType
            : throw new InvalidOperationException($"Encounter {encounterId} has no room type in v0.111.0.");
    }

    public static bool TryGet(string encounterId, out RoomType roomType) => RoomTypes.TryGetValue(encounterId, out roomType);

    public static bool ShouldGiveRewards(string encounterId) => !NoRewards.Contains(encounterId);
}
