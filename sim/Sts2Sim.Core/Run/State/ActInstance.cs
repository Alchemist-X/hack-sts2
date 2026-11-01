namespace Sts2Sim.Core.Run;

public sealed class ActInstance : ICanonical
{
    public ActInstance(ActModel def)
        : this(def, new RoomSet(), new List<string>())
    {
    }

    private ActInstance(ActModel def, RoomSet rooms, List<string> sharedAncientSubset)
    {
        ArgumentNullException.ThrowIfNull(def);
        Def = def;
        Rooms = rooms;
        SharedAncientSubset = sharedAncientSubset;
    }

    public ActModel Def { get; }

    public RoomSet Rooms { get; }

    public List<string> SharedAncientSubset { get; }

    public EncounterDef BossEncounter => Rooms.Boss ?? throw new InvalidOperationException($"Act {Def.Id} has no boss yet.");

    public EncounterDef? SecondBossEncounter => Rooms.SecondBoss;

    public bool HasSecondBoss => Rooms.HasSecondBoss;

    public void SetSharedAncientSubset(List<string> sharedAncientSubset)
    {
        ArgumentNullException.ThrowIfNull(sharedAncientSubset);
        SharedAncientSubset.Clear();
        SharedAncientSubset.AddRange(sharedAncientSubset);
    }

    public void GenerateRooms(Rng rng, UnlockProfile unlockState, bool isMultiplayer = false)
    {
        var eventIds = Def.AllEventIds.Concat(ActModel.AllSharedEventIds).ToList();
        if (!unlockState.IsEpochRevealed(ActModel.Event1EpochId))
        {
            eventIds.RemoveAll(e => ActModel.Event1EpochEventIds.Contains(e, StringComparer.Ordinal));
        }
        if (!unlockState.IsEpochRevealed(ActModel.Event2EpochId))
        {
            eventIds.RemoveAll(e => ActModel.Event2EpochEventIds.Contains(e, StringComparer.Ordinal));
        }
        if (!unlockState.IsEpochRevealed(ActModel.Event3EpochId))
        {
            eventIds.RemoveAll(e => ActModel.Event3EpochEventIds.Contains(e, StringComparer.Ordinal));
        }
        Rooms.events.AddRange(eventIds.UnstableShuffle(rng));
        var weakBag = new GrabBag<EncounterDef>();
        for (int num = 0; num < Def.WeakEncounterCount; num++)
        {
            if (!weakBag.Any())
            {
                foreach (var current in Def.AllWeakEncounters)
                {
                    weakBag.Add(current, 1.0);
                }
            }
            AddWithoutRepeatingTags(Rooms.normalEncounters, weakBag, rng);
        }
        var regularBag = new GrabBag<EncounterDef>();
        for (int num2 = Def.WeakEncounterCount; num2 < Def.GetNumberOfRooms(isMultiplayer); num2++)
        {
            if (!regularBag.Any())
            {
                foreach (var current in Def.AllRegularEncounters)
                {
                    regularBag.Add(current, 1.0);
                }
            }
            AddWithoutRepeatingTags(Rooms.normalEncounters, regularBag, rng);
        }
        var eliteBag = new GrabBag<EncounterDef>();
        for (int num3 = 0; num3 < ActModel.EliteEncounterCount; num3++)
        {
            if (!eliteBag.Any())
            {
                foreach (var current in Def.AllEliteEncounters)
                {
                    eliteBag.Add(current, 1.0);
                }
            }
            AddWithoutRepeatingTags(Rooms.eliteEncounters, eliteBag, rng);
        }
        Rooms.Boss = rng.NextItem(Def.AllBossEncounters);
        Rooms.AncientId = rng.NextItem(Def.GetUnlockedAncientIds(unlockState).Concat(SharedAncientSubset));
    }

    private static void AddWithoutRepeatingTags(List<EncounterDef> encounters, GrabBag<EncounterDef> grabBag, Rng rng)
    {
        var last = encounters.Count == 0 ? null : encounters[^1];
        var encounterModel = grabBag.GrabAndRemove(rng, e => !e.SharesTagsWith(last) && !ReferenceEquals(e, last));
        encounterModel ??= grabBag.GrabAndRemove(rng);
        if (encounterModel != null)
        {
            encounters.Add(encounterModel);
        }
    }

    public void ValidateRoomsAfterLoad(Rng rng)
    {
        if (Rooms.Boss == null)
        {
            Rooms.Boss = rng.NextItem(Def.AllBossEncounters.Where(e => e.Id != Rooms.SecondBoss?.Id));
        }
    }

    public void ApplyDiscoveryOrderModifications(UnlockProfile unlockState)
    {
        foreach (var current in Def.BossDiscoveryOrder)
        {
            if (!unlockState.HasSeenEncounter(current.Id))
            {
                Rooms.Boss = current;
                break;
            }
        }
        Def.ApplyActDiscoveryOrderModifications(this, unlockState);
    }

    public void SetBossEncounter(EncounterDef encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.RoomType != RoomType.Boss)
        {
            throw new ArgumentException("The encounter must be a boss.");
        }
        Rooms.Boss = encounter;
    }

    public void SetSecondBossEncounter(EncounterDef? encounter)
    {
        if (encounter != null && encounter.RoomType != RoomType.Boss)
        {
            throw new ArgumentException("The encounter must be a boss.");
        }
        Rooms.SecondBoss = encounter;
    }

    public void RemoveEventFromSet(string eventId) => Rooms.events.Remove(eventId);

    public EventModel PullAncient()
        => EventRegistry.Create(Rooms.AncientId ?? throw new InvalidOperationException("RoomSet.Ancient not set! You must call GenerateRooms"));

    public EventModel PullNextEvent(RunState runState)
    {
        Rooms.EnsureNextEventIsValid(runState);
        var eventModel = Hook.ModifyNextEvent(runState, EventRegistry.Create(Rooms.NextEventId));
        runState.AddVisitedEvent(eventModel);
        return eventModel;
    }

    public EncounterDef PullNextEncounter(RoomType roomType) => roomType switch
    {
        RoomType.Monster => Rooms.NextNormalEncounter,
        RoomType.Elite => Rooms.NextEliteEncounter,
        RoomType.Boss => Rooms.NextBossEncounter,
        _ => throw new ArgumentOutOfRangeException(nameof(roomType), roomType, null),
    };

    public void MarkRoomVisited(RoomType roomType) => Rooms.MarkVisited(roomType);

    public ActInstance DeepClone() => new(Def, Rooms.DeepClone(), new List<string>(SharedAncientSubset));

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.ActInstance);
        w.WriteString(Def.Id);
        w.WriteCount(SharedAncientSubset.Count);
        for (int i = 0; i < SharedAncientSubset.Count; i++)
        {
            w.WriteString(SharedAncientSubset[i]);
        }
        Rooms.WriteCanonical(w);
    }
}
