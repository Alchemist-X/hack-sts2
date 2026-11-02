namespace Sts2Sim.Core.Run;

public abstract partial class ActModel
{
    public const int EliteEncounterCount = 15;

    public const string Event1EpochId = "EVENT1_EPOCH";

    public const string Event2EpochId = "EVENT2_EPOCH";

    public const string Event3EpochId = "EVENT3_EPOCH";

    public const string DarvEpochId = "DARV_EPOCH";

    public const string DarvId = "DARV";

    public static readonly IReadOnlyList<string> GameActOrder = new[] { "OVERGROWTH", "UNDERDOCKS", "HIVE", "GLORY" };

    public static readonly IReadOnlyList<string> AllSharedEventIds = new[]
    {
        "BRAIN_LEECH", "CRYSTAL_SPHERE", "DOLL_ROOM", "FAKE_MERCHANT", "POTION_COURIER", "RANWID_THE_ELDER",
        "RELIC_TRADER", "ROOM_FULL_OF_CHEESE", "SELF_HELP_BOOK", "SLIPPERY_BRIDGE", "STONE_OF_ALL_TIME", "SYMBIOTE",
        "TEA_MASTER", "THE_FUTURE_OF_POTIONS", "THE_LEGENDS_WERE_TRUE", "THIS_OR_THAT", "WAR_HISTORIAN_REPY", "WELCOME_TO_WONGOS"
    };

    public static readonly IReadOnlyList<string> AllSharedAncientIds = new[] { DarvId };

    public static readonly IReadOnlyList<string> Event1EpochEventIds = new[] { "TRASH_HEAP" };

    public static readonly IReadOnlyList<string> Event2EpochEventIds = new[] { "REFLECTIONS" };

    public static readonly IReadOnlyList<string> Event3EpochEventIds = new[] { "COLORFUL_PHILOSOPHERS" };

    private static readonly Lazy<IReadOnlyList<IReadOnlyList<ActModel>>> ActsByIndexTable = new(BuildActsByIndex);

    private readonly Lazy<EncounterDef[]> _allEncounters;
    private readonly Lazy<EncounterDef[]> _allWeakEncounters;
    private readonly Lazy<EncounterDef[]> _allRegularEncounters;
    private readonly Lazy<EncounterDef[]> _allEliteEncounters;
    private readonly Lazy<EncounterDef[]> _allBossEncounters;

    protected ActModel()
    {
        _allEncounters = new(() => GenerateAllEncounters().ToArray());
        _allWeakEncounters = new(() => AllEncounters.Where(e => e != null && e.RoomType == RoomType.Monster && e.IsWeak).ToArray());
        _allRegularEncounters = new(() => AllEncounters.Where(e => e != null && e.RoomType == RoomType.Monster && !e.IsWeak).ToArray());
        _allEliteEncounters = new(() => AllEncounters.Where(e => e.RoomType == RoomType.Elite).ToArray());
        _allBossEncounters = new(() => AllEncounters.Where(e => e.RoomType == RoomType.Boss).ToArray());
    }

    public abstract string Id { get; }

    public abstract int Index { get; }

    public abstract bool IsDefault { get; }

    protected virtual int NumberOfWeakEncounters => 3;

    protected abstract int BaseNumberOfRooms { get; }

    public abstract IEnumerable<EncounterDef> BossDiscoveryOrder { get; }

    public abstract IEnumerable<string> AllAncientIds { get; }

    public abstract IEnumerable<string> AllEventIds { get; }

    public abstract IEnumerable<EncounterDef> GenerateAllEncounters();

    public virtual IReadOnlyList<string> DeclaredEncounterIds => Array.Empty<string>();

    public abstract bool IsUnlocked(UnlockProfile unlockState);

    public abstract IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state);

    public abstract MapPointTypeCounts GetMapPointTypes(Rng mapRng);

    protected internal virtual void ApplyActDiscoveryOrderModifications(ActInstance act, UnlockProfile unlockState)
    {
    }

    public int WeakEncounterCount => NumberOfWeakEncounters;

    public IReadOnlyList<EncounterDef> AllEncounters => _allEncounters.Value;

    public IReadOnlyList<EncounterDef> AllWeakEncounters => _allWeakEncounters.Value;

    public IReadOnlyList<EncounterDef> AllRegularEncounters => _allRegularEncounters.Value;

    public IReadOnlyList<EncounterDef> AllEliteEncounters => _allEliteEncounters.Value;

    public IReadOnlyList<EncounterDef> AllBossEncounters => _allBossEncounters.Value;

    public static IReadOnlyList<IReadOnlyList<ActModel>> ActsByIndex => ActsByIndexTable.Value;

    public static List<string> GetSharedAncientIds(UnlockProfile unlockState)
    {
        var ids = new List<string>(AllSharedAncientIds);
        if (!unlockState.IsEpochRevealed(DarvEpochId))
        {
            ids.Remove(DarvId);
        }
        return ids;
    }

    public int GetNumberOfRooms(bool isMultiplayer)
    {
        int num = BaseNumberOfRooms;
        if (isMultiplayer)
        {
            num--;
        }
        return num;
    }

    public int GetNumberOfFloors(bool isMultiplayer) => GetNumberOfRooms(isMultiplayer) + 2;

    public ActMap CreateMap(RunState runState, bool replaceTreasureWithElites) => StandardActMap.CreateFor(runState, replaceTreasureWithElites);

    public static IEnumerable<ActModel> GetRandomList(Rng rng, UnlockProfile unlockState, RunProfileProgress progress, bool isMultiplayer)
        => GetRandomList(ActsByIndex, rng, unlockState, progress, isMultiplayer);

    public static IEnumerable<ActModel> GetRandomList(IReadOnlyList<IReadOnlyList<ActModel>> actsByIndex, Rng rng, UnlockProfile unlockState, RunProfileProgress progress, bool isMultiplayer)
    {
        var list = new List<ActModel>();
        for (int i = 0; i < actsByIndex.Count; i++)
        {
            ActModel? actModel = null;
            var unlocked = new List<ActModel>();
            foreach (var current in actsByIndex[i])
            {
                if (current.IsUnlocked(unlockState))
                {
                    if (!current.IsDefault && !isMultiplayer && !progress.HasDiscoveredAct(current.Id))
                    {
                        actModel = current;
                        break;
                    }
                    unlocked.Add(current);
                }
            }
            actModel ??= rng.NextItem((IReadOnlyList<ActModel>)unlocked) ?? throw new InvalidOperationException($"No unlocked acts for index {i}!");
            list.Add(actModel);
        }
        return list;
    }

    public static IReadOnlyList<ActModel> GetDefaultList() => GetDefaultList(ActsByIndex);

    public static IReadOnlyList<ActModel> GetDefaultList(IReadOnlyList<IReadOnlyList<ActModel>> actsByIndex)
    {
        var list = new List<ActModel>();
        for (int i = 0; i < actsByIndex.Count; i++)
        {
            ActModel? actModel = null;
            foreach (var current in actsByIndex[i])
            {
                if (current.IsDefault)
                {
                    actModel = current;
                    break;
                }
            }
            list.Add(actModel ?? throw new InvalidOperationException($"No default act for index {i}!"));
        }
        return list;
    }

    public static IReadOnlyList<IReadOnlyList<ActModel>> BuildActsByIndex(IEnumerable<ActModel> actsInGameOrder)
    {
        var byIndex = new List<List<ActModel>>();
        foreach (var current in actsInGameOrder)
        {
            if (current.Index >= 0)
            {
                for (int i = byIndex.Count; i <= current.Index; i++)
                {
                    byIndex.Add(new List<ActModel>());
                }
                byIndex[current.Index].Add(current);
            }
        }
        var result = new IReadOnlyList<ActModel>[byIndex.Count];
        for (int i = 0; i < byIndex.Count; i++)
        {
            result[i] = byIndex[i].ToArray();
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<ActModel>> BuildActsByIndex()
    {
        var acts = new List<ActModel>();
        for (int i = 0; i < GameActOrder.Count; i++)
        {
            if (ActRegistry.TryGet(GameActOrder[i], out var def))
            {
                acts.Add(def);
            }
        }
        foreach (var id in ActRegistry.AllIds)
        {
            if (!GameActOrder.Contains(id, StringComparer.Ordinal))
            {
                acts.Add(ActRegistry.Get(id));
            }
        }
        return BuildActsByIndex(acts);
    }
}
