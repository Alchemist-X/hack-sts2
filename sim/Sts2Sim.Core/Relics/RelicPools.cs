using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Relics;

public sealed class RelicPoolData
{
    private readonly string[] _relicIds;

    internal RelicPoolData(string id, string[] relicIds)
    {
        Id = id;
        _relicIds = relicIds;
        AllRelicIds = Array.AsReadOnly(relicIds);
    }

    public string Id { get; }

    public ReadOnlyCollection<string> AllRelicIds { get; }

    public bool Contains(string relicId) => Array.IndexOf(_relicIds, relicId) >= 0;
}

public static class RelicPools
{
    public const string IroncladRelicPool = "IRONCLAD_RELIC_POOL";
    public const string SilentRelicPool = "SILENT_RELIC_POOL";
    public const string RegentRelicPool = "REGENT_RELIC_POOL";
    public const string NecrobinderRelicPool = "NECROBINDER_RELIC_POOL";
    public const string DefectRelicPool = "DEFECT_RELIC_POOL";
    public const string DeprecatedRelicPool = "DEPRECATED_RELIC_POOL";
    public const string EventRelicPool = "EVENT_RELIC_POOL";
    public const string FallbackRelicPool = "FALLBACK_RELIC_POOL";
    public const string SharedRelicPool = "SHARED_RELIC_POOL";

    private static readonly RelicPoolData[] Pools =
    {
        new(IroncladRelicPool, new[]
        {
            "BRIMSTONE", "BURNING_BLOOD", "CHARONS_ASHES", "DEMON_TONGUE", "PAPER_PHROG", "RED_SKULL",
            "RUINED_HELMET", "SELF_FORMING_CLAY"
        }),
        new(SilentRelicPool, new[]
        {
            "HELICAL_DART", "NINJA_SCROLL", "PAPER_KRANE", "RING_OF_THE_SNAKE", "SNECKO_SKULL", "TINGSHA",
            "TOUGH_BANDAGES", "TWISTED_FUNNEL"
        }),
        new(RegentRelicPool, new[]
        {
            "DIVINE_RIGHT", "FENCING_MANUAL", "GALACTIC_DUST", "LUNAR_PASTRY", "MINI_REGENT", "ORANGE_DOUGH",
            "REGALITE", "VITRUVIAN_MINION"
        }),
        new(NecrobinderRelicPool, new[]
        {
            "BIG_HAT", "BONE_FLUTE", "BOOK_REPAIR_KNIFE", "BOOKMARK", "BOUND_PHYLACTERY", "FUNERARY_MASK",
            "IVORY_TILE", "UNDYING_SIGIL"
        }),
        new(DefectRelicPool, new[]
        {
            "CRACKED_CORE", "DATA_DISK", "EMOTION_CHIP", "GOLD_PLATED_CABLES", "POWER_CELL", "METRONOME",
            "RUNIC_CAPACITOR", "SYMBIOTIC_VIRUS"
        }),
        new(DeprecatedRelicPool, new[]
        {
            "DEPRECATED_RELIC"
        }),
        new(EventRelicPool, new[]
        {
            "ALCHEMICAL_COFFER", "ARCANE_SCROLL", "ARCHAIC_TOOTH", "ASTROLABE", "BEAUTIFUL_BRACELET", "BIG_MUSHROOM",
            "BIIIG_HUG", "BING_BONG", "BLACK_BLOOD", "BLACK_STAR", "BLESSED_ANTLER", "BLOOD_SOAKED_ROSE", "BONE_TEA",
            "BOOMING_CONCH", "BRILLIANT_SCARF", "BYRDPIP", "CALLING_BELL", "CHOICES_PARADOX", "CHOSEN_CHEESE",
            "CLAWS", "CROSSBOW", "CURSED_PEARL", "DARKSTONE_PERIAPT", "DAUGHTER_OF_THE_WIND", "DELICATE_FROND",
            "DIAMOND_DIADEM", "DISTINGUISHED_CAPE", "DIVINE_DESTINY", "DOWSING_ROD", "DREAM_CATCHER", "DRIFTWOOD",
            "DUSTY_TOME", "ECTOPLASM", "ELECTRIC_SHRYMP", "EMBER_TEA", "EMPTY_CAGE", "FAKE_ANCHOR",
            "FAKE_BLOOD_VIAL", "FAKE_HAPPY_FLOWER", "FAKE_LEES_WAFFLE", "FAKE_MANGO", "FAKE_MERCHANTS_RUG",
            "FAKE_ORICHALCUM", "FAKE_SNECKO_EYE", "FAKE_STRIKE_DUMMY", "FAKE_VENERABLE_TEA_SET", "FIDDLE",
            "FISHING_ROD", "FORGOTTEN_SOUL", "FRAGRANT_MUSHROOM", "FUR_COAT", "GLASS_EYE", "GLITTER",
            "GOLDEN_COMPASS", "GOLDEN_PEARL", "HAND_DRILL", "HEFTY_TABLET", "HISTORY_COURSE", "INFUSED_CORE",
            "IRON_CLUB", "JEWELED_MASK", "JEWELRY_BOX", "KALEIDOSCOPE", "LARGE_CAPSULE", "LASTING_CANDY",
            "LAVA_ROCK", "LEAD_PAPERWEIGHT", "LEAFY_POULTICE", "LORDS_PARASOL", "LOST_COFFER", "LOST_WISP",
            "MASSIVE_SCROLL", "MAW_BANK", "MEAT_CLEAVER", "MR_STRUGGLES", "MUSIC_BOX", "NEOWS_BONES",
            "NEOWS_SACRIFICE", "NEOWS_TALISMAN", "NEOWS_TORMENT", "NEW_LEAF", "NUTRITIOUS_OYSTER", "NUTRITIOUS_SOUP",
            "PAELS_BLOOD", "PAELS_CLAW", "PAELS_EYE", "PAELS_FLESH", "PAELS_GROWTH", "PAELS_HORN", "PAELS_LEGION",
            "PAELS_TEARS", "PAELS_TOOTH", "PAELS_WING", "PANDORAS_BOX", "PHIAL_HOLSTER", "PHILOSOPHERS_STONE",
            "PHYLACTERY_UNBOUND", "POLLINOUS_CORE", "POMANDER", "PRECARIOUS_SHEARS", "PRECISE_SCISSORS",
            "PRESERVED_FOG", "PRISMATIC_GEM", "PUMPKIN_CANDLE", "RADIANT_PEARL", "RAZOR_TOOTH", "RING_OF_THE_DRAKE",
            "ROYAL_POISON", "RUNIC_PYRAMID", "SAI", "SAND_CASTLE", "SCROLL_BOXES", "SEA_GLASS", "SEAL_OF_GOLD",
            "SERE_TALON", "SIGNET_RING", "SILKEN_TRESS", "SILVER_CRUCIBLE", "SMALL_CAPSULE", "SNECKO_EYE", "SOZU",
            "SPARKLING_ROUGE", "SPIKED_GAUNTLETS", "STONE_HUMIDIFIER", "STORYBOOK", "SWORD_OF_JADE",
            "SWORD_OF_STONE", "TANXS_WHISTLE", "TEA_OF_DISCOURTESY", "THE_BOOT", "THROWING_AXE", "TOASTY_MITTENS",
            "TOUCH_OF_OROBAS", "TOY_BOX", "TRI_BOOMERANG", "VELVET_CHOKER", "WAR_HAMMER", "WHISPERING_EARRING",
            "WINGED_BOOTS", "WONGO_CUSTOMER_APPRECIATION_BADGE", "WONGOS_MYSTERY_TICKET", "YUMMY_COOKIE"
        }),
        new(FallbackRelicPool, new[]
        {
            "CIRCLET"
        }),
        new(SharedRelicPool, new[]
        {
            "AKABEKO", "AMETHYST_AUBERGINE", "ANCHOR", "ART_OF_WAR", "BAG_OF_MARBLES", "BAG_OF_PREPARATION",
            "BEATING_REMNANT", "BELLOWS", "BELT_BUCKLE", "BLOOD_VIAL", "BOOK_OF_FIVE_RINGS", "BOWLER_HAT", "BREAD",
            "BRONZE_SCALES", "BURNING_STICKS", "CANDELABRA", "CAPTAINS_WHEEL", "CAULDRON", "CENTENNIAL_PUZZLE",
            "CHANDELIER", "CHEMICAL_X", "CLOAK_CLASP", "DINGY_RUG", "DOLLYS_MIRROR", "DRAGON_FRUIT",
            "ETERNAL_FEATHER", "FESTIVE_POPPER", "FRESNEL_LENS", "FROZEN_EGG", "GAMBLING_CHIP", "GAME_PIECE",
            "GHOST_SEED", "GIRYA", "GNARLED_HAMMER", "GORGET", "GREMLIN_HORN", "HAPPY_FLOWER", "HORN_CLEAT",
            "ICE_CREAM", "INTIMIDATING_HELMET", "JOSS_PAPER", "JUZU_BRACELET", "KIFUDA", "KUNAI", "KUSARIGAMA",
            "LANTERN", "LASTING_CANDY", "LAVA_LAMP", "LEES_WAFFLE", "LETTER_OPENER", "LIZARD_TAIL", "LOOMING_FRUIT",
            "LUCKY_FYSH", "MANGO", "MEAL_TICKET", "MEAT_ON_THE_BONE", "MEMBERSHIP_CARD", "MERCURY_HOURGLASS",
            "MINIATURE_CANNON", "MINIATURE_TENT", "MOLTEN_EGG", "MUMMIFIED_HAND", "MYSTIC_LIGHTER", "NUNCHAKU",
            "ODDLY_SMOOTH_STONE", "OLD_COIN", "ORICHALCUM", "ORNAMENTAL_FAN", "ORRERY", "PANTOGRAPH",
            "PARRYING_SHIELD", "PEAR", "PEN_NIB", "PENDULUM", "PERMAFROST", "PETRIFIED_TOAD", "PLANISPHERE",
            "POCKETWATCH", "POTION_BELT", "PRAYER_WHEEL", "PUNCH_DAGGER", "RAINBOW_RING", "RAZOR_TOOTH", "RED_MASK",
            "REGAL_PILLOW", "REPTILE_TRINKET", "RINGING_TRIANGLE", "RIPPLE_BASIN", "ROYAL_STAMP", "SCREAMING_FLAGON",
            "SHOVEL", "SHURIKEN", "SLING_OF_COURAGE", "SPARKLING_ROUGE", "STONE_CALENDAR", "STONE_CRACKER",
            "STRAWBERRY", "STRIKE_DUMMY", "STURDY_CLAMP", "THE_ABACUS", "THE_COURIER", "TINY_MAILBOX", "TOOLBOX",
            "TOXIC_EGG", "TUNGSTEN_ROD", "TUNING_FORK", "UNCEASING_TOP", "UNSETTLING_LAMP", "VAJRA", "VAMBRACE",
            "VENERABLE_TEA_SET", "VERY_HOT_COCOA", "VEXING_PUZZLEBOX", "WAR_PAINT", "WHETSTONE",
            "WHITE_BEAST_STATUE", "WHITE_STAR", "WING_CHARM"
        })
    };

    public static readonly ReadOnlyCollection<RelicPoolData> AllRelicPools = Array.AsReadOnly(Pools);

    public static RelicPoolData Get(string poolId)
    {
        for (int i = 0; i < Pools.Length; i++)
        {
            if (Pools[i].Id == poolId)
            {
                return Pools[i];
            }
        }
        throw new KeyNotFoundException($"No relic pool with id '{poolId}'");
    }

    public static string PoolOf(string relicId)
    {
        for (int i = 0; i < Pools.Length; i++)
        {
            if (Pools[i].Contains(relicId))
            {
                return Pools[i].Id;
            }
        }
        throw new InvalidOperationException($"Relic {relicId} is in no relic pool (Models/RelicModel.cs:192: First over ModelDb.AllRelicPools).");
    }
}
