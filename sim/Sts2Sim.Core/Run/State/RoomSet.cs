namespace Sts2Sim.Core.Run;

public sealed class RoomSet : ICanonical
{
    public readonly List<string> events = new();

    public int eventsVisited;

    public readonly List<EncounterDef> normalEncounters = new();

    public int normalEncountersVisited;

    public readonly List<EncounterDef> eliteEncounters = new();

    public int eliteEncountersVisited;

    public int bossEncountersVisited;

    public string? AncientId { get; set; }

    public EncounterDef? Boss { get; set; }

    public EncounterDef? SecondBoss { get; set; }

    public bool HasAncient => AncientId != null;

    public bool HasSecondBoss => SecondBoss != null;

    public string NextEventId => events[eventsVisited % events.Count];

    public EncounterDef NextNormalEncounter => normalEncounters[normalEncountersVisited % normalEncounters.Count];

    public EncounterDef NextEliteEncounter => eliteEncounters[eliteEncountersVisited % eliteEncounters.Count];

    public EncounterDef NextBossEncounter
    {
        get
        {
            if (bossEncountersVisited != 0 && SecondBoss != null)
            {
                return SecondBoss;
            }
            return Boss ?? throw new InvalidOperationException("RoomSet.Boss not set! You must call GenerateRooms");
        }
    }

    public void MarkVisited(RoomType roomType)
    {
        switch (roomType)
        {
            case RoomType.Monster:
                normalEncountersVisited++;
                break;
            case RoomType.Elite:
                eliteEncountersVisited++;
                break;
            case RoomType.Event:
                eventsVisited++;
                break;
            case RoomType.Boss:
                bossEncountersVisited++;
                break;
        }
    }

    public void EnsureNextEventIsValid(RunState runState)
    {
        if (events.Count == 0)
        {
            return;
        }
        for (int i = 0; i < events.Count; i++)
        {
            var id = NextEventId;
            if (IsEventAllowed(runState, id) && !runState.VisitedEventIds.Contains(id))
            {
                return;
            }
            eventsVisited++;
        }
    }

    private static bool IsEventAllowed(RunState runState, string eventId)
        => EventRegistry.Models.Contains(eventId) ? EventRegistry.Create(eventId).IsAllowed(runState) : UnportedEventRules.IsAllowed(eventId, runState);

    /// <summary>
    /// Determinization only: a fresh uniform order for the events not reached yet. <c>GenerateRooms</c> builds the list
    /// with one uniform shuffle and pulls walk it front to back, so the unvisited tail is uniform given the visited
    /// prefix. The event at <c>eventsVisited</c> stays put when it was already pulled (the current event room).
    /// </summary>
    internal void ReshuffleUnvisitedEvents(Rng rng, IReadOnlySet<string> visitedEventIds)
    {
        int start = eventsVisited;
        if (start < events.Count && visitedEventIds.Contains(events[start]))
        {
            start++;
        }
        for (int i = events.Count - 1; i > start; i--)
        {
            int j = start + rng.NextInt(i - start + 1);
            (events[i], events[j]) = (events[j], events[i]);
        }
    }

    public RoomSet DeepClone()
    {
        var clone = new RoomSet
        {
            eventsVisited = eventsVisited,
            normalEncountersVisited = normalEncountersVisited,
            eliteEncountersVisited = eliteEncountersVisited,
            bossEncountersVisited = bossEncountersVisited,
            AncientId = AncientId,
            Boss = Boss,
            SecondBoss = SecondBoss
        };
        clone.events.AddRange(events);
        clone.normalEncounters.AddRange(normalEncounters);
        clone.eliteEncounters.AddRange(eliteEncounters);
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.RoomSet);
        w.WriteCount(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            w.WriteString(events[i]);
        }
        w.WriteInt32(eventsVisited);
        WriteEncounters(w, normalEncounters);
        w.WriteInt32(normalEncountersVisited);
        WriteEncounters(w, eliteEncounters);
        w.WriteInt32(eliteEncountersVisited);
        w.WriteInt32(bossEncountersVisited);
        w.WriteString(AncientId);
        w.WriteString(Boss?.Id);
        w.WriteString(SecondBoss?.Id);
    }

    private static void WriteEncounters(CanonicalWriter w, List<EncounterDef> encounters)
    {
        w.WriteCount(encounters.Count);
        for (int i = 0; i < encounters.Count; i++)
        {
            w.WriteString(encounters[i].Id);
        }
    }
}
