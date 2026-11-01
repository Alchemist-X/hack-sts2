namespace Sts2Sim.Core.Run;

public sealed class GoldenPathActMap : ActMap
{
    private const int _width = 7;

    private const int _middle = 3;

    private static readonly MapPointType[] DefaultPointTypes =
    {
        MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite,
        MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Treasure,
        MapPointType.Unknown, MapPointType.Treasure, MapPointType.Unknown, MapPointType.Shop,
        MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite,
    };

    private readonly MapPoint?[,] _grid;

    public GoldenPathActMap(RunState runState)
        : this(runState.Players.Count > 1)
    {
    }

    public GoldenPathActMap(bool isMultiplayer)
    {
        List<MapPointType> types = DefaultPointTypes.ToList();
        if (isMultiplayer)
        {
            types.RemoveAt(2);
        }
        _grid = new MapPoint?[_width, types.Count + 1];
        BossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount()) { PointType = MapPointType.Boss };
        StartingMapPoint = new MapPoint(GetColumnCount() / 2, 0) { PointType = MapPointType.Ancient };
        for (int i = 0; i < types.Count; i++)
        {
            var mapPoint = new MapPoint(_middle, i + 1);
            _grid[_middle, i + 1] = mapPoint;
            mapPoint.PointType = types[i];
            if (i > 0)
            {
                _grid[_middle, i]!.AddChildPoint(mapPoint);
            }
        }
        startMapPoints.Add(_grid[_middle, 1]!);
        _grid[_middle, GetRowCount() - 1]!.AddChildPoint(BossMapPoint);
        StartingMapPoint.AddChildPoint(_grid[_middle, 1]!);
    }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    protected override MapPoint?[,] Grid => _grid;
}
