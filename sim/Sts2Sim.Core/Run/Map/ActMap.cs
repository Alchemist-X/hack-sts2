namespace Sts2Sim.Core.Run;

public abstract partial class ActMap : ICanonical
{
    public readonly HashSet<MapPoint> startMapPoints = new();

    private MapQuest[] _quests = Array.Empty<MapQuest>();

    public abstract MapPoint BossMapPoint { get; }

    public abstract MapPoint StartingMapPoint { get; }

    public virtual MapPoint? SecondBossMapPoint => null;

    protected abstract MapPoint?[,] Grid { get; }

    public int GetColumnCount() => Grid.GetLength(0);

    public int GetRowCount() => Grid.GetLength(1);

    public IEnumerable<MapPoint> GetAllMapPoints()
    {
        for (int c = 0; c < GetColumnCount(); c++)
        {
            for (int r = 0; r < Grid.GetLength(1); r++)
            {
                MapPoint? mapPoint = Grid[c, r];
                if (mapPoint != null)
                {
                    yield return mapPoint;
                }
            }
        }
    }

    public IEnumerable<MapPoint> GetPointsInRow(int row)
    {
        if (row < 0 || row >= Grid.GetLength(1))
        {
            yield break;
        }
        for (int c = 0; c < GetColumnCount(); c++)
        {
            MapPoint? mapPoint = Grid[c, row];
            if (mapPoint != null)
            {
                yield return mapPoint;
            }
        }
    }

    public virtual MapPoint? GetPoint(MapCoord coord) => GetPoint(coord.col, coord.row);

    public MapPoint? GetPoint(int col, int row)
    {
        if (col == BossMapPoint.coord.col && row == BossMapPoint.coord.row)
        {
            return BossMapPoint;
        }
        if (SecondBossMapPoint != null && col == SecondBossMapPoint.coord.col && row == SecondBossMapPoint.coord.row)
        {
            return SecondBossMapPoint;
        }
        if (col == StartingMapPoint.coord.col && row == StartingMapPoint.coord.row)
        {
            return StartingMapPoint;
        }
        if (col >= 0 && col < Grid.GetLength(0) && row >= 0 && row < Grid.GetLength(1))
        {
            return Grid[col, row];
        }
        return null;
    }

    public bool IsInMap(MapPoint mapPoint)
    {
        if (mapPoint.PointType == MapPointType.Ancient || mapPoint.PointType == MapPointType.Boss)
        {
            return true;
        }
        int col = mapPoint.coord.col;
        int row = mapPoint.coord.row;
        if (col < 0 || col >= Grid.GetLength(0) || row < 0 || row >= Grid.GetLength(1))
        {
            return false;
        }
        return Grid[col, row] != null;
    }

    public bool HasPoint(MapCoord coord)
    {
        if (coord.col == BossMapPoint.coord.col && coord.row == BossMapPoint.coord.row)
        {
            return true;
        }
        if (SecondBossMapPoint != null && coord.col == SecondBossMapPoint.coord.col && coord.row == SecondBossMapPoint.coord.row)
        {
            return true;
        }
        if (coord.col == StartingMapPoint.coord.col && coord.row == StartingMapPoint.coord.row)
        {
            return true;
        }
        if (coord.col < 0 || coord.col >= Grid.GetLength(0))
        {
            return false;
        }
        if (coord.row < 0 || coord.row >= Grid.GetLength(1))
        {
            return false;
        }
        return Grid[coord.col, coord.row] != null;
    }

    public IReadOnlyList<string> GetQuests(MapCoord coord)
    {
        List<string>? result = null;
        foreach (MapQuest quest in _quests)
        {
            if (quest.Coord == coord)
            {
                (result ??= new List<string>()).Add(quest.OwnerKey);
            }
        }
        return result ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public IReadOnlyList<MapQuest> AllQuests => _quests;

    public ActMap WithQuestAdded(MapCoord coord, string ownerKey)
    {
        var copy = (ActMap)MemberwiseClone();
        var quests = new MapQuest[_quests.Length + 1];
        _quests.CopyTo(quests, 0);
        quests[^1] = new MapQuest(coord, ownerKey);
        copy._quests = quests;
        return copy;
    }

    public ActMap WithQuestRemoved(MapCoord coord, string ownerKey)
    {
        int index = Array.FindIndex(_quests, q => q.Coord == coord && string.Equals(q.OwnerKey, ownerKey, StringComparison.Ordinal));
        if (index < 0)
        {
            return this;
        }
        var copy = (ActMap)MemberwiseClone();
        var quests = new List<MapQuest>(_quests);
        quests.RemoveAt(index);
        copy._quests = quests.ToArray();
        return copy;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MapCanonicalTags.ActMap);
        w.WriteInt32(GetColumnCount());
        w.WriteInt32(GetRowCount());
        int count = 0;
        foreach (MapPoint _ in GetAllMapPoints())
        {
            count++;
        }
        w.WriteCount(count);
        foreach (MapPoint point in GetAllMapPoints())
        {
            WritePoint(w, point);
        }
        WritePoint(w, BossMapPoint);
        MapPoint? secondBoss = SecondBossMapPoint;
        w.WriteBool(secondBoss != null);
        if (secondBoss != null)
        {
            WritePoint(w, secondBoss);
        }
        WritePoint(w, StartingMapPoint);
        w.WriteCount(startMapPoints.Count);
        foreach (MapPoint start in startMapPoints)
        {
            start.coord.WriteCanonical(w);
        }
        w.WriteTag(MapCanonicalTags.QuestOverlay);
        w.WriteCount(_quests.Length);
        foreach (MapQuest quest in _quests)
        {
            quest.Coord.WriteCanonical(w);
            w.WriteString(quest.OwnerKey);
        }
    }

    private static void WritePoint(CanonicalWriter w, MapPoint point)
    {
        point.coord.WriteCanonical(w);
        w.WriteByte((byte)point.PointType);
        w.WriteBool(point.CanBeModified);
        w.WriteCount(point.Children.Count);
        foreach (MapPoint child in point.Children)
        {
            child.coord.WriteCanonical(w);
        }
    }
}

public readonly record struct MapQuest(MapCoord Coord, string OwnerKey);
