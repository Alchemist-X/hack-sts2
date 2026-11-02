namespace Sts2Sim.Core.Run;

public static class MapTravel
{
    public static IEnumerable<MapPoint> GetTravelablePointsFrom(RunState runState, MapPoint currentPoint)
    {
        if (Hook.ShouldAllowFreeTravel(runState))
        {
            return RequireMap(runState).GetPointsInRow(currentPoint.coord.row + 1);
        }
        return currentPoint.Children;
    }

    public static void GetLegalMapPoints(RunState runState, List<MapPoint> buffer)
    {
        buffer.Clear();
        ActMap map = RequireMap(runState);
        List<MapCoord> visited = runState.VisitedMapCoords;
        if (visited.Count == 0)
        {
            buffer.Add(map.StartingMapPoint);
            return;
        }
        MapCoord last = visited[visited.Count - 1];
        MapPoint? secondBoss = map.SecondBossMapPoint;
        if (secondBoss != null && last == map.BossMapPoint.coord)
        {
            buffer.Add(secondBoss);
            return;
        }
        if (last.row == map.GetRowCount() - 1)
        {
            buffer.Add(map.BossMapPoint);
            return;
        }
        MapPoint? current = map.GetPoint(last);
        if (current == null)
        {
            buffer.Add(map.StartingMapPoint);
            return;
        }
        foreach (MapPoint point in GetTravelablePointsFrom(runState, current))
        {
            buffer.Add(point);
        }
        buffer.Sort(CompareByColumn);
    }

    public static List<MapPoint> GetLegalMapPoints(RunState runState)
    {
        var buffer = new List<MapPoint>();
        GetLegalMapPoints(runState, buffer);
        return buffer;
    }

    private static int CompareByColumn(MapPoint a, MapPoint b)
    {
        int byCol = a.coord.col.CompareTo(b.coord.col);
        return byCol != 0 ? byCol : a.coord.row.CompareTo(b.coord.row);
    }

    private static ActMap RequireMap(RunState runState)
        => runState.Map ?? throw new InvalidOperationException("RunState.Map is not set.");
}
