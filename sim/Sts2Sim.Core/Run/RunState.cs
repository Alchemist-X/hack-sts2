namespace Sts2Sim.Core.Run;

public sealed partial class RunState : ICanonical
{
    private const int DefaultPlayerCapacity = 1;

    private CombatState? _combat;

    private int _currentActIndex;

    private RunState(List<PlayerState> players, RunRngSet rng, UnlockProfile unlocks, int ascension, List<ModifierModel> modifiers, ChoiceRuntime choice, FidelityLog fidelity)
    {
        Players = players;
        Rng = rng;
        Unlocks = unlocks;
        Ascension = ascension;
        Modifiers = modifiers;
        Choice = choice;
        Fidelity = fidelity;
    }

    public List<PlayerState> Players { get; }

    public CombatState? Combat
    {
        get => _combat;
        set
        {
            if (value != null && !ReferenceEquals(value.Run, this))
            {
                throw new InvalidOperationException("A combat can be attached only to the run it was built on.");
            }
            _combat = value;
        }
    }

    public RunRngSet Rng { get; }

    public UnlockProfile Unlocks { get; }

    public int Ascension { get; }

    public List<ModifierModel> Modifiers { get; }

    public ChoiceRuntime Choice { get; }

    public FidelityLog Fidelity { get; }

    public int NextCardInstanceId { get; set; }

    public int AllocateCardInstanceId() => NextCardInstanceId++;

    public CardInstance CloneCard(CardInstance mutableCard)
    {
        ArgumentNullException.ThrowIfNull(mutableCard);
        var clone = mutableCard.CloneForCombat(AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
    }

    public RunConfig? Config { get; set; }

    public List<ActInstance> Acts { get; private set; } = new();

    public int CurrentActIndex
    {
        get => _currentActIndex;
        set
        {
            if (_currentActIndex != value)
            {
                VisitedMapCoords.Clear();
                ActFloor = 0;
                NextRoomId = 0;
                _currentActIndex = value;
            }
        }
    }

    public int ActFloor { get; set; }

    public int NextRoomId { get; set; }

    public ActMap? Map { get; set; }

    public List<MapCoord> VisitedMapCoords { get; private set; } = new();

    public List<MapPointHistoryEntry> MapPointHistory { get; private set; } = new();

    public HashSet<string> VisitedEventIds { get; private set; } = new(StringComparer.Ordinal);

    public ExtraRunFields ExtraFields { get; private set; } = new();

    public RunOddsSet? Odds { get; set; }

    public RelicGrabBag? SharedRelicGrabBag { get; set; }

    public List<AbstractRoom> CurrentRooms { get; private set; } = new();

    public List<RewardsSet> RewardsStack { get; private set; } = new();

    public ActInstance Act => Acts[CurrentActIndex];

    public int TotalFloor => MapPointHistory.Count;

    public const string NeowEpochId = "NEOW_EPOCH";

    public MapCoord? CurrentMapCoord => VisitedMapCoords.Count != 0 ? VisitedMapCoords[^1] : null;

    public MapPointHistoryEntry? CurrentMapPointHistoryEntry => MapPointHistory.Count != 0 ? MapPointHistory[^1] : null;

    public bool HasAscension(AscensionLevel level) => Ascension >= (int)level;

    public int GetPlayerSlotIndex(PlayerState player) => Players.IndexOf(player);

    public static RunState CreateForNewRun(PlayerState player, IReadOnlyList<ActModel> acts, int ascension, string seed, UnlockProfile? unlocks = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(acts);
        ArgumentNullException.ThrowIfNull(seed);
        var rng = new RunRngSet(seed);
        var players = new List<PlayerState>(DefaultPlayerCapacity) { player };
        var rs = new RunState(players, rng, unlocks ?? UnlockProfile.All, ascension, new List<ModifierModel>(), new ChoiceRuntime(), new FidelityLog());
        for (int i = 0; i < acts.Count; i++)
        {
            rs.Acts.Add(new ActInstance(acts[i]));
        }
        rs.Odds = new RunOddsSet(rng.UnknownMapPoint);
        rs.SharedRelicGrabBag = new RelicGrabBag(refreshAllowed: true);
        for (int i = 0; i < players.Count; i++)
        {
            rs.InitializeSeed(players[i], seed);
        }
        return rs;
    }

    public void InitializeSeed(PlayerState player, string seed)
    {
        var playerRng = PlayerRngSet.ForPlayer(seed, GetPlayerSlotIndex(player));
        player.PlayerRng = playerRng;
        player.PlayerOdds = new PlayerOddsSet(playerRng, Ascension);
    }

    public void SetStartedWithNeowFlag() => ExtraFields.StartedWithNeow = Unlocks.IsEpochRevealed(NeowEpochId);

    public void ApplyAscensionEffects(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.AscensionEffectsApplied)
        {
            return;
        }
        if (HasAscension(AscensionLevel.TightBelt))
        {
            player.SubtractFromMaxPotionCount(1);
        }
        if (HasAscension(AscensionLevel.AscendersBane))
        {
            var def = CardRegistry.Get(AscendersBane.CardId);
            var ascendersBane = new CardInstance(def, AllocateCardInstanceId());
            ascendersBane.AttachTo(player);
            def.AfterCreated(ascendersBane);
            player.Deck.Add(ascendersBane);
        }
        player.AscensionEffectsApplied = true;
    }

    public void GenerateRooms()
    {
        var upFront = Rng.UpFront;
        var sharedAncients = ActModel.GetSharedAncientIds(Unlocks).UnstableShuffle(upFront);
        for (int a = 1; a < Acts.Count; a++)
        {
            int num = upFront.NextInt(sharedAncients.Count + 1);
            var subset = sharedAncients.Take(num).ToList();
            sharedAncients = sharedAncients.Except(subset, StringComparer.Ordinal).ToList();
            Acts[a].SetSharedAncientSubset(subset);
        }
        bool isMultiplayer = Players.Count > 1;
        for (int i = 0; i < Acts.Count; i++)
        {
            var act = Acts[i];
            act.GenerateRooms(upFront, Unlocks, isMultiplayer);
            act.ApplyDiscoveryOrderModifications(Unlocks);
            if (i == Acts.Count - 1 && HasAscension(AscensionLevel.DoubleBoss))
            {
                var bossId = act.BossEncounter.Id;
                act.SetSecondBossEncounter(upFront.NextItem(act.Def.AllBossEncounters.Where(e => e.Id != bossId)));
            }
        }
    }

    public bool AddVisitedMapCoord(MapCoord coord)
    {
        if (VisitedMapCoords.Contains(coord))
        {
            return false;
        }
        VisitedMapCoords.Add(coord);
        NextRoomId = 0;
        return true;
    }

    public void ClearVisitedMapCoords() => VisitedMapCoords.Clear();

    public void AddVisitedEvent(EventModel eventModel)
    {
        ArgumentNullException.ThrowIfNull(eventModel);
        VisitedEventIds.Add(eventModel.Id);
    }

    public MapPointHistoryEntry AppendToMapPointHistory(MapPointType mapPointType, RoomType initialRoomType, string? roomModelId)
    {
        var entry = new MapPointHistoryEntry(mapPointType) { ActIndex = CurrentActIndex };
        entry.Rooms.Add(new MapPointRoomHistoryEntry(initialRoomType, roomModelId));
        MapPointHistory.Add(entry);
        return entry;
    }

    public MapPointHistoryEntry? GetHistoryEntryFor(int actIndex, MapCoord? coord)
    {
        if (!coord.HasValue)
        {
            return null;
        }
        int row = coord.Value.row;
        int seen = 0;
        var history = MapPointHistory;
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].ActIndex == actIndex)
            {
                if (seen == row)
                {
                    return history[i];
                }
                seen++;
            }
        }
        return null;
    }

    public static RunState CreateStandalone(PlayerState player, RunRngSet rng, int ascension, UnlockProfile? unlocks = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!player.AscensionEffectsApplied)
        {
            if (ascension >= (int)AscensionLevel.TightBelt)
            {
                player.SubtractFromMaxPotionCount(1);
            }
            player.AscensionEffectsApplied = true;
        }
        var players = new List<PlayerState>(DefaultPlayerCapacity) { player };
        return new RunState(players, rng, unlocks ?? UnlockProfile.All, ascension, new List<ModifierModel>(), new ChoiceRuntime(), new FidelityLog());
    }

    public RunState DeepClone()
    {
        var clone = CloneWithoutCombat();
        var combat = Combat;
        if (combat != null)
        {
            clone.Combat = combat.CloneInto(clone, this);
        }
        return clone;
    }

    internal RunState CloneWithoutCombat()
    {
        var sourcePlayers = Players;
        var players = new List<PlayerState>(Math.Max(sourcePlayers.Count, DefaultPlayerCapacity));
        for (int i = 0; i < sourcePlayers.Count; i++)
        {
            players.Add(sourcePlayers[i].DeepClone());
        }
        var sourceModifiers = Modifiers;
        var modifiers = new List<ModifierModel>(sourceModifiers.Count);
        for (int i = 0; i < sourceModifiers.Count; i++)
        {
            modifiers.Add(sourceModifiers[i].DeepClone());
        }
        var rng = Rng is null ? null! : Rng.DeepClone();
        var clone = new RunState(players, rng, Unlocks, Ascension, modifiers, Choice.DeepClone(), Fidelity.DeepClone())
        {
            NextCardInstanceId = NextCardInstanceId
        };
        CopyRunLayerInto(clone);
        return clone;
    }

    private void CopyRunLayerInto(RunState clone)
    {
        clone.Config = Config;
        var acts = Acts;
        clone.Acts = new List<ActInstance>(acts.Count);
        for (int i = 0; i < acts.Count; i++)
        {
            clone.Acts.Add(acts[i].DeepClone());
        }
        clone._currentActIndex = _currentActIndex;
        clone.ActFloor = ActFloor;
        clone.NextRoomId = NextRoomId;
        clone.Map = Map;
        clone.VisitedMapCoords = new List<MapCoord>(VisitedMapCoords);
        var history = MapPointHistory;
        clone.MapPointHistory = new List<MapPointHistoryEntry>(history.Count);
        for (int i = 0; i < history.Count; i++)
        {
            clone.MapPointHistory.Add(history[i].DeepClone());
        }
        clone.VisitedEventIds = new HashSet<string>(VisitedEventIds, StringComparer.Ordinal);
        clone.ExtraFields = ExtraFields.DeepClone();
        clone.Odds = Odds?.CloneFor(clone.Rng);
        clone.SharedRelicGrabBag = SharedRelicGrabBag?.DeepClone();
        var rooms = CurrentRooms;
        clone.CurrentRooms = new List<AbstractRoom>(rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            clone.CurrentRooms.Add(rooms[i].CloneFor(clone));
        }
        var rewards = RewardsStack;
        clone.RewardsStack = new List<RewardsSet>(rewards.Count);
        for (int i = 0; i < rewards.Count; i++)
        {
            var set = rewards[i];
            var room = set.Room;
            int roomIndex = room == null ? -1 : rooms.IndexOf(room);
            clone.RewardsStack.Add(set.CloneFor(clone.Players[Players.IndexOf(set.Player)], roomIndex < 0 ? null : clone.CurrentRooms[roomIndex]));
        }
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        WriteRunFields(w);
        var combat = Combat;
        w.WriteBool(combat != null);
        combat?.WriteCombatFields(w);
    }

    internal void WriteRunFields(CanonicalWriter w)
    {
        w.WriteTag(RunCanonicalTags.RunState);
        w.WriteInt32(Ascension);
        w.WriteInt32(NextCardInstanceId);
        Unlocks.WriteCanonical(w);
        var players = Players;
        w.WriteTag(RunCanonicalTags.PlayerList);
        w.WriteCount(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            players[i].WriteCanonical(w);
        }
        var rng = Rng;
        w.WriteBool(rng is not null);
        rng?.WriteCanonical(w);
        var modifiers = Modifiers;
        w.WriteTag(RunCanonicalTags.ModifierList);
        w.WriteCount(modifiers.Count);
        for (int i = 0; i < modifiers.Count; i++)
        {
            modifiers[i].WriteCanonical(w);
        }
        Choice.WriteCanonical(w);
        Fidelity.WriteCanonical(w);
        WriteRunLayer(w);
    }

    private void WriteRunLayer(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.RunLayer);
        var config = Config;
        w.WriteBool(config != null);
        config?.WriteCanonical(w);
        var acts = Acts;
        w.WriteTag(RunStateCanonicalTags.Acts);
        w.WriteCount(acts.Count);
        for (int i = 0; i < acts.Count; i++)
        {
            acts[i].WriteCanonical(w);
        }
        w.WriteInt32(CurrentActIndex);
        w.WriteInt32(ActFloor);
        w.WriteInt32(NextRoomId);
        var map = Map;
        w.WriteBool(map != null);
        map?.WriteCanonical(w);
        var coords = VisitedMapCoords;
        w.WriteTag(RunStateCanonicalTags.VisitedMapCoords);
        w.WriteCount(coords.Count);
        for (int i = 0; i < coords.Count; i++)
        {
            coords[i].WriteCanonical(w);
        }
        var history = MapPointHistory;
        w.WriteTag(RunStateCanonicalTags.MapPointHistory);
        w.WriteCount(history.Count);
        for (int i = 0; i < history.Count; i++)
        {
            history[i].WriteCanonical(w);
        }
        w.WriteTag(RunStateCanonicalTags.VisitedEventIds);
        var eventIds = VisitedEventIds.Count == 0 ? Array.Empty<string>() : VisitedEventIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        w.WriteCount(eventIds.Length);
        for (int i = 0; i < eventIds.Length; i++)
        {
            w.WriteString(eventIds[i]);
        }
        ExtraFields.WriteCanonical(w);
        var odds = Odds;
        w.WriteBool(odds != null);
        odds?.WriteCanonical(w);
        var sharedBag = SharedRelicGrabBag;
        w.WriteBool(sharedBag != null);
        sharedBag?.WriteCanonical(w);
        var rooms = CurrentRooms;
        w.WriteTag(RunStateCanonicalTags.RoomStack);
        w.WriteCount(rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            rooms[i].WriteCanonical(w);
        }
        var rewards = RewardsStack;
        w.WriteTag(RunStateCanonicalTags.RewardsStack);
        w.WriteCount(rewards.Count);
        for (int i = 0; i < rewards.Count; i++)
        {
            rewards[i].WriteCanonical(w);
        }
    }
}
