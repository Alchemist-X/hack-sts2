namespace Sts2Sim.Core.Run;

public sealed class SavedActMap : ActMap
{
    private readonly MapPoint?[,] _grid;

    public SavedActMap(SerializableActMap saved)
    {
        _grid = new MapPoint?[saved.GridWidth, saved.GridHeight];
        var lookup = new Dictionary<MapCoord, MapPoint>();
        foreach (SerializableMapPoint current in saved.Points)
        {
            MapPoint mapPoint = CreatePoint(current);
            _grid[current.Coord.col, current.Coord.row] = mapPoint;
            lookup[current.Coord] = mapPoint;
        }
        BossMapPoint = CreatePoint(saved.BossPoint);
        lookup[saved.BossPoint.Coord] = BossMapPoint;
        StartingMapPoint = CreatePoint(saved.StartingPoint);
        lookup[saved.StartingPoint.Coord] = StartingMapPoint;
        if (saved.SecondBossPoint != null)
        {
            SecondBossMapPoint = CreatePoint(saved.SecondBossPoint);
            lookup[saved.SecondBossPoint.Coord] = SecondBossMapPoint;
        }
        foreach (SerializableMapPoint point in saved.Points)
        {
            WireChildren(point, lookup);
        }
        WireChildren(saved.BossPoint, lookup);
        WireChildren(saved.StartingPoint, lookup);
        if (saved.SecondBossPoint != null)
        {
            WireChildren(saved.SecondBossPoint, lookup);
        }
        if (saved.StartMapPointCoords == null)
        {
            return;
        }
        foreach (MapCoord coord in saved.StartMapPointCoords)
        {
            if (lookup.TryGetValue(coord, out MapPoint? start))
            {
                startMapPoints.Add(start);
            }
        }
    }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    public override MapPoint? SecondBossMapPoint { get; }

    protected override MapPoint?[,] Grid => _grid;

    private static MapPoint CreatePoint(SerializableMapPoint saved)
        => new(saved.Coord.col, saved.Coord.row) { PointType = saved.PointType, CanBeModified = saved.CanBeModified };

    private static void WireChildren(SerializableMapPoint savedPoint, Dictionary<MapCoord, MapPoint> lookup)
    {
        if (savedPoint.ChildCoords == null)
        {
            return;
        }
        MapPoint mapPoint = lookup[savedPoint.Coord];
        foreach (MapCoord coord in savedPoint.ChildCoords)
        {
            if (lookup.TryGetValue(coord, out MapPoint? child))
            {
                mapPoint.AddChildPoint(child);
            }
        }
    }
}
