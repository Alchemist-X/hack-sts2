namespace Sts2Sim.Core.Run;

public sealed partial class MapPointHistoryEntry : ICanonical
{
    public MapPointHistoryEntry(MapPointType mapPointType)
    {
        MapPointType = mapPointType;
    }

    public MapPointType MapPointType { get; }

    public List<MapPointRoomHistoryEntry> Rooms { get; } = new();

    public int GoldGained { get; set; }

    public bool HasRoomOfType(RoomType roomType) => throw new NotImplementedException("W2 R1: MapPointHistoryEntry.HasRoomOfType");

    public MapPointHistoryEntry DeepClone() => throw new NotImplementedException("W2 R1: MapPointHistoryEntry.DeepClone");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R1: MapPointHistoryEntry.WriteCanonical");
}

public sealed record MapPointRoomHistoryEntry(RoomType RoomType, string? ModelId, IReadOnlyList<string> MonsterIds, int TurnsTaken);
