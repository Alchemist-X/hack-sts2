namespace Sts2Sim.Core.Run;

public static class RoomStack
{
    public static AbstractRoom? CurrentRoom(RunState rs) => rs.CurrentRooms.Count == 0 ? null : rs.CurrentRooms[^1];

    public static AbstractRoom? BaseRoom(RunState rs) => rs.CurrentRooms.Count == 0 ? null : rs.CurrentRooms[0];

    public static MapPointHistoryEntry? CurrentMapPointHistoryEntry(RunState rs) => rs.MapPointHistory.Count == 0 ? null : rs.MapPointHistory[^1];

    public static MapCoord? CurrentMapCoord(RunState rs) => rs.VisitedMapCoords.Count == 0 ? null : rs.VisitedMapCoords[^1];

    public static AbstractRoom CreateRoom(RunState rs, RoomType roomType, MapPointType mapPointType) => CreateRoom(rs, roomType, mapPointType, null);

    public static AbstractRoom CreateRoom(RunState rs, RoomType roomType, MapPointType mapPointType, object? model)
    {
        ArgumentNullException.ThrowIfNull(rs);
        switch (roomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
                return model is EncounterDef encounter
                    ? new CombatRoom(encounter)
                    : new CombatRoom(rs.Act.PullNextEncounter(roomType), roomType);
            case RoomType.Treasure:
                return new TreasureRoom(rs.CurrentActIndex);
            case RoomType.Shop:
                return new MerchantRoom();
            case RoomType.Event:
                if (model is EventModel eventModel)
                {
                    return new EventRoom(eventModel);
                }
                return new EventRoom(mapPointType == MapPointType.Ancient ? rs.Act.PullAncient(rs) : rs.Act.PullNextEvent(rs));
            case RoomType.RestSite:
                return new RestSiteRoom();
            case RoomType.Map:
                return new MapRoom();
            default:
                throw new InvalidOperationException($"Unexpected RoomType: {roomType}");
        }
    }

    public static void EnterRoom(RunState rs, AbstractRoom room)
    {
        ExitCurrentRooms(rs);
        EnterRoomInternal(rs, room, false);
    }

    public static void EnterRoomWithoutExitingCurrentRoom(RunState rs, AbstractRoom room)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(room);
        CurrentMapPointHistoryEntry(rs)?.Rooms.Add(new MapPointRoomHistoryEntry(room.RoomType, room.ModelId, new List<string>(), 0));
        EnterRoomInternal(rs, room, false);
    }

    public static void EnterRoomInternal(RunState rs, AbstractRoom room, bool isRestoringRoomStackBase)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(room);
        bool preFinished = room is CombatRoom or EventRoom && room.IsPreFinished;
        bool runExternalEffects = !(isRestoringRoomStackBase || preFinished);
        if (rs.CurrentRooms.Contains(room))
        {
            throw new InvalidOperationException("Already in this room.");
        }
        rs.CurrentRooms.Add(room);
        if (runExternalEffects && room is not MapRoom)
        {
            Hook.BeforeRoomEntered(rs, room);
        }
        room.Enter(rs, isRestoringRoomStackBase);
        if (runExternalEffects && rs.CurrentRooms.Count == 1)
        {
            rs.Act.MarkRoomVisited(room.RoomType);
        }
    }

    public static void ExitCurrentRooms(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        while (rs.CurrentRooms.Count > 0)
        {
            ExitCurrentRoom(rs);
        }
    }

    public static AbstractRoom ExitCurrentRoom(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        BeforeLeavingRoom(rs);
        var rooms = rs.CurrentRooms;
        if (rooms.Count == 0)
        {
            throw new InvalidOperationException("Not in any rooms.");
        }
        var room = rooms[^1];
        rooms.RemoveAt(rooms.Count - 1);
        room.Exit(rs);
        return room;
    }

    public static void BeforeLeavingRoom(RunState rs)
    {
        var stack = rs.RewardsStack;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var rewards = stack[i].Rewards;
            for (int j = 0; j < rewards.Count; j++)
            {
                if (!rewards[j].SuccessfullySelected)
                {
                    rewards[j].OnSkipped(rs);
                }
            }
        }
        stack.Clear();
    }

    public static void ResumePreviousRoom(RunState rs)
    {
        var exited = ExitCurrentRoom(rs);
        var current = CurrentRoom(rs) ?? throw new InvalidOperationException("No room to resume after exiting the top room.");
        current.Resume(rs, exited);
    }

    public static void Offer(RunState rs, RewardsSet set)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(set);
        rs.RewardsStack.Add(set);
    }
}
