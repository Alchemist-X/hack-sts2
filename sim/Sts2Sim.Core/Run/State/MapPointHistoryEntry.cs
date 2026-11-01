namespace Sts2Sim.Core.Run;

public sealed partial class MapPointHistoryEntry : ICanonical
{
    public MapPointHistoryEntry(MapPointType mapPointType)
    {
        MapPointType = mapPointType;
    }

    public MapPointType MapPointType { get; }

    public int ActIndex { get; init; }

    public List<MapPointRoomHistoryEntry> Rooms { get; } = new();

    public int GoldGained { get; set; }

    public bool HasRoomOfType(RoomType roomType)
    {
        var rooms = Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].RoomType == roomType)
            {
                return true;
            }
        }
        return false;
    }

    public MapPointHistoryEntry DeepClone()
    {
        var clone = new MapPointHistoryEntry(MapPointType)
        {
            ActIndex = ActIndex,
            GoldGained = GoldGained
        };
        clone.Rooms.AddRange(Rooms);
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.HistoryEntry);
        w.WriteByte((byte)MapPointType);
        w.WriteInt32(ActIndex);
        var rooms = Rooms;
        w.WriteCount(rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            w.WriteByte((byte)room.RoomType);
            w.WriteString(room.ModelId);
            var monsters = room.MonsterIds;
            w.WriteCount(monsters.Count);
            for (int j = 0; j < monsters.Count; j++)
            {
                w.WriteString(monsters[j]);
            }
            w.WriteInt32(room.TurnsTaken);
        }
        w.WriteInt32(GoldGained);
    }
}

public sealed record MapPointRoomHistoryEntry(RoomType RoomType, string? ModelId, IReadOnlyList<string> MonsterIds, int TurnsTaken)
{
    public MapPointRoomHistoryEntry(RoomType roomType, string? modelId)
        : this(roomType, modelId, Array.Empty<string>(), 0)
    {
    }
}
