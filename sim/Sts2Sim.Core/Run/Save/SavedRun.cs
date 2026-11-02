namespace Sts2Sim.Core.Run;

public enum SavePoint : byte
{
    EnteringMapPoint,
    PostCombatRewards,
    AncientFinished,
    NoVisitedCoord
}

public sealed class SavedRun
{
    public const int SupportedSchemaVersion = 20;

    public const string StandardGameMode = "standard";

    public int SchemaVersion { get; init; }

    public string Json { get; init; } = string.Empty;

    public List<SavedAct> Acts { get; init; } = new();

    public List<SavedModifier> Modifiers { get; init; } = new();

    public long? DailyTime { get; init; }

    public int CurrentActIndex { get; init; }

    public List<string> EventsSeen { get; init; } = new();

    public SavedRoom? PreFinishedRoom { get; init; }

    public SavedRunOdds Odds { get; init; } = new();

    public SavedRelicGrabBag SharedRelicGrabBag { get; init; } = new();

    public List<SavedPlayer> Players { get; init; } = new();

    public SavedRunRng Rng { get; init; } = new();

    public List<MapCoord> VisitedMapCoords { get; init; } = new();

    public List<List<SavedMapPointHistoryEntry>> MapPointHistory { get; init; } = new();

    public int Ascension { get; init; }

    public string GameMode { get; init; } = StandardGameMode;

    public SavedExtraRunFields ExtraFields { get; init; } = new();

    public string? PlatformType { get; init; }
}

public sealed class SavedRunRng
{
    public string Seed { get; init; } = string.Empty;

    public Dictionary<string, RngSnapshot> Rngs { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SavedPlayerRng
{
    public ulong Seed { get; init; }

    public Dictionary<string, RngSnapshot> Rngs { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SavedRunOdds
{
    public float UnknownMapPointMonsterOddsValue { get; init; }

    public float UnknownMapPointEliteOddsValue { get; init; }

    public float UnknownMapPointTreasureOddsValue { get; init; }

    public float UnknownMapPointShopOddsValue { get; init; }
}

public sealed class SavedPlayerOdds
{
    public float CardRarityOddsValue { get; init; }

    public float PotionRewardOddsValue { get; init; }
}

public sealed class SavedRelicGrabBag
{
    public Dictionary<RelicRarity, List<string>> RelicIdLists { get; init; } = new();
}

public sealed class SavedExtraRunFields
{
    public bool StartedWithNeow { get; init; }

    public int TestSubjectKills { get; init; }

    public bool FreedRepy { get; init; }
}

public sealed class SavedExtraPlayerFields
{
    public int CardShopRemovalsUsed { get; init; }

    public int WongoPoints { get; init; }
}

public sealed class SavedAct
{
    public string Id { get; init; } = string.Empty;

    public SavedRoomSet Rooms { get; init; } = new();

    public SavedActMapData? SavedMap { get; init; }
}

public sealed class SavedRoomSet
{
    public List<string> EventIds { get; init; } = new();

    public int EventsVisited { get; init; }

    public List<string> NormalEncounterIds { get; init; } = new();

    public int NormalEncountersVisited { get; init; }

    public List<string> EliteEncounterIds { get; init; } = new();

    public int EliteEncountersVisited { get; init; }

    public int BossEncountersVisited { get; init; }

    public string? BossId { get; init; }

    public string? SecondBossId { get; init; }

    public string? AncientId { get; init; }
}

public sealed class SavedActMapData
{
    public List<SavedMapPoint> Points { get; init; } = new();

    public SavedMapPoint? Boss { get; init; }

    public SavedMapPoint? SecondBoss { get; init; }

    public SavedMapPoint? Start { get; init; }

    public List<MapCoord> StartCoords { get; init; } = new();

    public int Width { get; init; }

    public int Height { get; init; }
}

public sealed class SavedMapPoint
{
    public MapCoord Coord { get; init; }

    public MapPointType Type { get; init; }

    public bool CanModify { get; init; }

    public List<MapCoord> Children { get; init; } = new();
}

public sealed class SavedRoom
{
    public RoomType RoomType { get; init; }

    public string? EncounterId { get; init; }

    public string? EventId { get; init; }

    public bool IsPreFinished { get; init; }

    public float GoldProportion { get; init; }

    public Dictionary<ulong, List<SavedReward>> ExtraRewards { get; init; } = new();

    public string? ParentEventId { get; init; }

    public bool ShouldResumeParentEvent { get; init; } = true;

    public Dictionary<string, string> EncounterState { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SavedReward
{
    public string Json { get; init; } = string.Empty;
}

public sealed class SavedModifier
{
    public string Id { get; init; } = string.Empty;

    public SavedProperties? Props { get; init; }
}

public sealed class SavedPlayer
{
    public string CharacterId { get; init; } = string.Empty;

    public int CurrentHp { get; init; }

    public int MaxHp { get; init; }

    public int MaxEnergy { get; init; }

    public int MaxPotionSlotCount { get; init; }

    public int Gold { get; init; }

    public int BaseOrbSlotCount { get; init; }

    public ulong NetId { get; init; }

    public List<SavedCard> Deck { get; init; } = new();

    public List<SavedRelic> Relics { get; init; } = new();

    public List<SavedPotion> Potions { get; init; } = new();

    public SavedPlayerRng Rng { get; init; } = new();

    public SavedPlayerOdds Odds { get; init; } = new();

    public SavedRelicGrabBag RelicGrabBag { get; init; } = new();

    public SavedExtraPlayerFields ExtraFields { get; init; } = new();

    public SavedUnlockState UnlockState { get; init; } = new();
}

public sealed class SavedUnlockState
{
    public List<string> UnlockedEpochs { get; init; } = new();

    public List<string> EncountersSeen { get; init; } = new();

    public int NumberOfRuns { get; init; }
}

public sealed class SavedCard
{
    public string Id { get; init; } = string.Empty;

    public int CurrentUpgradeLevel { get; init; }

    public SavedEnchantment? Enchantment { get; init; }

    public SavedProperties? Props { get; init; }

    public int? FloorAddedToDeck { get; init; }
}

public sealed class SavedEnchantment
{
    public string Id { get; init; } = string.Empty;

    public int Amount { get; init; }

    public SavedProperties? Props { get; init; }
}

public sealed class SavedRelic
{
    public string Id { get; init; } = string.Empty;

    public SavedProperties? Props { get; init; }

    public int? FloorAddedToDeck { get; init; }
}

public sealed class SavedPotion
{
    public string Id { get; init; } = string.Empty;

    public int SlotIndex { get; init; }
}

public sealed class SavedProperties
{
    public List<KeyValuePair<string, int>> Ints { get; init; } = new();

    public List<KeyValuePair<string, bool>> Bools { get; init; } = new();

    public List<KeyValuePair<string, string>> Strings { get; init; } = new();

    public List<KeyValuePair<string, int[]>> IntArrays { get; init; } = new();

    public List<KeyValuePair<string, string>> ModelIds { get; init; } = new();

    public List<KeyValuePair<string, SavedCard>> Cards { get; init; } = new();

    public List<KeyValuePair<string, SavedCard[]>> CardArrays { get; init; } = new();

    public bool IsEmpty => Ints.Count == 0 && Bools.Count == 0 && Strings.Count == 0 && IntArrays.Count == 0
        && ModelIds.Count == 0 && Cards.Count == 0 && CardArrays.Count == 0;
}

public sealed class SavedMapPointHistoryEntry
{
    public MapPointType MapPointType { get; init; }

    public List<SavedMapPointRoom> Rooms { get; init; } = new();

    public int? GoldGained { get; init; }
}

public sealed class SavedMapPointRoom
{
    public RoomType RoomType { get; init; }

    public string? ModelId { get; init; }

    public List<string> MonsterIds { get; init; } = new();

    public int TurnsTaken { get; init; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SavedPropertyAttribute : Attribute
{
}

public sealed record ImportOptions
{
    public static readonly ImportOptions Default = new();

    public RunProfileProgress Progress { get; init; } = RunProfileProgress.Veteran;

    public ContentFilter Content { get; init; } = ContentFilter.All;
}

public sealed record RunImportResult(RunSim? Sim, SavePoint? EntryPoint, IReadOnlyList<string> UnsupportedReasons)
{
    public bool IsSupported => Sim != null && UnsupportedReasons.Count == 0;
}

public sealed class ImportedRun
{
    public ImportedRun(RunState state, SavePoint entryPoint, SavedRoom? preFinishedRoom)
    {
        State = state;
        EntryPoint = entryPoint;
        PreFinishedRoom = preFinishedRoom;
    }

    public RunState State { get; }

    public SavePoint EntryPoint { get; }

    public SavedRoom? PreFinishedRoom { get; }
}
