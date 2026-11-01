using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public static partial class CardPools
{
    public const string IroncladCharacterId = "IRONCLAD";
    public const string SilentCharacterId = "SILENT";
    public const string RegentCharacterId = "REGENT";
    public const string NecrobinderCharacterId = "NECROBINDER";
    public const string DefectCharacterId = "DEFECT";

    private static readonly Lazy<PoolTable> Table = new(BuildTable);

    public static CardPool Ironclad => Table.Value.Ironclad;

    public static CardPool Silent => Table.Value.Silent;

    public static CardPool Regent => Table.Value.Regent;

    public static CardPool Necrobinder => Table.Value.Necrobinder;

    public static CardPool Defect => Table.Value.Defect;

    public static CardPool Colorless => Table.Value.Colorless;

    public static CardPool Curse => Table.Value.Curse;

    public static CardPool Deprecated => Table.Value.Deprecated;

    public static CardPool Event => Table.Value.Event;

    public static CardPool Quest => Table.Value.Quest;

    public static CardPool Status => Table.Value.Status;

    public static CardPool Token => Table.Value.Token;

    public static IReadOnlyList<string> AllCharacterIds => Table.Value.CharacterIds;

    public static IReadOnlyList<CardPool> AllCharacterCardPools => Table.Value.CharacterPools;

    public static IReadOnlyList<CardPool> AllSharedCardPools => Table.Value.SharedPools;

    public static IReadOnlyList<CardPool> AllCardPools => Table.Value.AllPools;

    public static CardPool ForCharacter(string characterId)
        => TryGetForCharacter(characterId, out var pool)
            ? pool
            : throw new KeyNotFoundException($"No card pool for character '{characterId}'");

    public static bool TryGetForCharacter(string characterId, out CardPool pool)
    {
        var ids = Table.Value.CharacterIds;
        var pools = Table.Value.CharacterPools;
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], characterId, StringComparison.Ordinal))
            {
                pool = pools[i];
                return true;
            }
        }
        pool = null!;
        return false;
    }

    public static CardPool? PoolOf(string cardId)
    {
        var pools = Table.Value.AllPools;
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].Contains(cardId))
            {
                return pools[i];
            }
        }
        return null;
    }

    public static CardMultiplayerConstraint MultiplayerConstraintFor(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        return rs.Players.Count <= 1 ? CardMultiplayerConstraint.SingleplayerOnly : CardMultiplayerConstraint.MultiplayerOnly;
    }

    private static CardEpochGate Gate(string epochId, params string[] cardIds) => new(epochId, Array.AsReadOnly(cardIds));

    private static PoolTable BuildTable()
    {
        var noGates = Array.Empty<CardEpochGate>();
        var ironclad = new CardPool("IRONCLAD_CARD_POOL", "ironclad", false, IroncladIds, new[]
        {
            Gate("IRONCLAD2_EPOCH", "MOLTEN_FIST", "CRUELTY", "DOMINATE"),
            Gate("IRONCLAD5_EPOCH", "CINDER", "PACTS_END", "DRUM_OF_BATTLE"),
            Gate("IRONCLAD7_EPOCH", "BLOOD_WALL", "TEAR_ASUNDER", "INFERNO"),
        });
        var silent = new CardPool("SILENT_CARD_POOL", "silent", false, SilentIds, new[]
        {
            Gate("SILENT2_EPOCH", "SNAKEBITE", "BUBBLE_BUBBLE", "ACCELERANT"),
            Gate("SILENT5_EPOCH", "REFLEX", "MASTER_PLANNER", "HAND_TRICK"),
            Gate("SILENT7_EPOCH", "HIDDEN_DAGGERS", "BLADE_OF_INK", "PHANTOM_BLADES"),
        });
        var regent = new CardPool("REGENT_CARD_POOL", "regent", false, RegentIds, new[]
        {
            Gate("REGENT2_EPOCH", "SPOILS_OF_BATTLE", "SWORD_SAGE", "FURNACE"),
            Gate("REGENT5_EPOCH", "BEGONE", "ARSENAL", "SUPERMASSIVE"),
            Gate("REGENT7_EPOCH", "PATTER", "HEAVENLY_DRILL", "LUNAR_BLAST"),
        });
        var necrobinder = new CardPool("NECROBINDER_CARD_POOL", "necrobinder", false, NecrobinderIds, new[]
        {
            Gate("NECROBINDER2_EPOCH", "SCOURGE", "OBLIVION", "COUNTDOWN"),
            Gate("NECROBINDER5_EPOCH", "AFTERLIFE", "SACRIFICE", "CALCIFY"),
            Gate("NECROBINDER7_EPOCH", "SCULPTING_STRIKE", "VEILPIERCER", "BANSHEES_CRY"),
        });
        var defect = new CardPool("DEFECT_CARD_POOL", "defect", false, DefectIds, new[]
        {
            Gate("DEFECT2_EPOCH", "LOOP", "NULL", "CONSUMING_SHADOW"),
            Gate("DEFECT5_EPOCH", "BARRAGE", "FLAK_CANNON", "SMOKESTACK"),
            Gate("DEFECT7_EPOCH", "TURBO", "SCAVENGE", "HELIX_DRILL"),
        });
        var colorless = new CardPool("COLORLESS_CARD_POOL", "colorless", true, ColorlessIds, new[]
        {
            Gate("COLORLESS1_EPOCH", "AUTOMATION", "ENTROPY", "CATASTROPHE"),
            Gate("COLORLESS2_EPOCH", "ETERNAL_ARMOR", "JACKPOT", "PREP_TIME"),
            Gate("COLORLESS3_EPOCH", "REND", "BEAT_DOWN", "PROWESS"),
            Gate("COLORLESS4_EPOCH", "ALCHEMIZE", "NOSTALGIA", "SCRAWL"),
            Gate("COLORLESS5_EPOCH", "SPLASH", "ANOINTED", "CALAMITY"),
        });
        var curse = new CardPool("CURSE_CARD_POOL", "curse", false, CurseIds, noGates);
        var deprecated = new CardPool("DEPRECATED_CARD_POOL", "token", true, DeprecatedIds, noGates);
        var eventPool = new CardPool("EVENT_CARD_POOL", "event", true, EventIds, noGates);
        var quest = new CardPool("QUEST_CARD_POOL", "quest", false, QuestIds, noGates);
        var status = new CardPool("STATUS_CARD_POOL", "status", false, StatusIds, noGates);
        var token = new CardPool("TOKEN_CARD_POOL", "token", true, TokenIds, noGates);
        var characterIds = Array.AsReadOnly(new[] { IroncladCharacterId, SilentCharacterId, RegentCharacterId, NecrobinderCharacterId, DefectCharacterId });
        var characterPools = Array.AsReadOnly(new[] { ironclad, silent, regent, necrobinder, defect });
        var sharedPools = Array.AsReadOnly(new[] { colorless, curse, deprecated, eventPool, quest, status, token });
        var allPools = Array.AsReadOnly(new[] { ironclad, silent, regent, necrobinder, defect, colorless, curse, deprecated, eventPool, quest, status, token });
        return new PoolTable(ironclad, silent, regent, necrobinder, defect, colorless, curse, deprecated, eventPool, quest, status, token, characterIds, characterPools, sharedPools, allPools);
    }

    private sealed record PoolTable(
        CardPool Ironclad,
        CardPool Silent,
        CardPool Regent,
        CardPool Necrobinder,
        CardPool Defect,
        CardPool Colorless,
        CardPool Curse,
        CardPool Deprecated,
        CardPool Event,
        CardPool Quest,
        CardPool Status,
        CardPool Token,
        ReadOnlyCollection<string> CharacterIds,
        ReadOnlyCollection<CardPool> CharacterPools,
        ReadOnlyCollection<CardPool> SharedPools,
        ReadOnlyCollection<CardPool> AllPools);
}
