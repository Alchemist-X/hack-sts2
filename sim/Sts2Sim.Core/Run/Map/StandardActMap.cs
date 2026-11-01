namespace Sts2Sim.Core.Run;

public sealed class StandardActMap : ActMap
{
    public const int maxElites = 15;

    private const int _iterations = 7;

    private const int _mapWidth = 7;

    private readonly MapPointTypeCounts _pointTypeCounts;

    private readonly int _mapLength;

    private readonly Rng _rng;

    private MapPoint?[,] _grid;

    public StandardActMap(Rng mapRng, ActModel actModel, bool isMultiplayer, bool shouldReplaceTreasureWithElites, bool hasSecondBoss = false, MapPointTypeCounts? mapPointTypeCountsOverride = null, bool enablePruning = true, int ascension = 0)
        : this(mapRng, actModel.GetNumberOfRooms(isMultiplayer), mapPointTypeCountsOverride ?? WithAscensionElites(actModel.GetMapPointTypes(mapRng), ascension), shouldReplaceTreasureWithElites, hasSecondBoss, enablePruning)
    {
    }

    public StandardActMap(Rng mapRng, int numberOfRooms, MapPointTypeCounts pointTypeCounts, bool shouldReplaceTreasureWithElites, bool hasSecondBoss = false, bool enablePruning = true)
    {
        _mapLength = numberOfRooms + 1;
        ShouldReplaceTreasureWithElites = shouldReplaceTreasureWithElites;
        _grid = new MapPoint?[_mapWidth, _mapLength];
        _rng = mapRng;
        _pointTypeCounts = pointTypeCounts;
        BossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount());
        StartingMapPoint = new MapPoint(GetColumnCount() / 2, 0);
        if (hasSecondBoss)
        {
            SecondBossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount() + 1);
        }
        GenerateMap();
        AssignPointTypes();
        if (enablePruning)
        {
            MapPathPruning.PruneAndRepair(_grid, startMapPoints, this, _pointTypeCounts, _rng, IsValidPointType);
        }
        _grid = MapPostProcessing.CenterGrid(_grid);
        _grid = MapPostProcessing.SpreadAdjacentMapPoints(_grid);
        _grid = MapPostProcessing.StraightenPaths(_grid);
    }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    public override MapPoint? SecondBossMapPoint { get; }

    public bool ShouldReplaceTreasureWithElites { get; }

    protected override MapPoint?[,] Grid => _grid;

    public static StandardActMap CreateFor(RunState runState, bool replaceTreasureWithElites)
    {
        return new StandardActMap(
            new Rng(runState.Rng.Seed, $"act_{runState.CurrentActIndex + 1}_map"),
            runState.Act.Def,
            runState.Players.Count > 1,
            replaceTreasureWithElites,
            runState.Act.HasSecondBoss,
            ascension: runState.Ascension);
    }

    public static MapPointTypeCounts WithAscensionElites(MapPointTypeCounts counts, int ascension)
    {
        counts.NumOfElites = MapPointTypeCounts.EliteCountFor(ascension);
        return counts;
    }

    private MapPoint GetOrCreateMapPoint(MapCoord coord) => GetOrCreatePoint(coord.col, coord.row);

    private MapPoint GetOrCreatePoint(int col, int row)
    {
        MapPoint? point = GetPoint(col, row);
        if (point != null)
        {
            return point;
        }
        point = new MapPoint(col, row);
        _grid[col, row] = point;
        return point;
    }

    private void PathGenerate(MapPoint startingPoint)
    {
        MapPoint mapPoint = startingPoint;
        while (mapPoint.coord.row < _mapLength - 1)
        {
            MapCoord coord = GenerateNextCoord(mapPoint);
            MapPoint orCreateMapPoint = GetOrCreateMapPoint(coord);
            mapPoint.AddChildPoint(orCreateMapPoint);
            mapPoint = orCreateMapPoint;
        }
    }

    private MapCoord GenerateNextCoord(MapPoint current)
    {
        int col = current.coord.col;
        int left = Math.Max(0, col - 1);
        int right = Math.Min(col + 1, 6);
        var directions = new List<int>(3) { -1, 0, 1 };
        directions.StableShuffle(_rng);
        foreach (int direction in directions)
        {
            int row = current.coord.row + 1;
            int target = direction switch
            {
                -1 => left,
                0 => col,
                1 => right,
                _ => throw new InvalidOperationException("This isn't possible"),
            };
            if (!HasInvalidCrossover(current, target))
            {
                return new MapCoord(target, row);
            }
        }
        throw new InvalidOperationException("Cannot find next node");
    }

    private bool HasInvalidCrossover(MapPoint current, int targetX)
    {
        int num = targetX - current.coord.col;
        if (num == 0 || num == 7)
        {
            return false;
        }
        MapPoint? mapPoint = _grid[targetX, current.coord.row];
        if (mapPoint == null)
        {
            return false;
        }
        foreach (MapPoint child in mapPoint.Children)
        {
            int num2 = child.coord.col - mapPoint.coord.col;
            if (num2 == -num)
            {
                return true;
            }
        }
        return false;
    }

    private void GenerateMap()
    {
        for (int i = 0; i < _iterations; i++)
        {
            MapPoint orCreatePoint = GetOrCreatePoint(_rng.NextInt(0, 7), 1);
            if (i == 1)
            {
                while (startMapPoints.Contains(orCreatePoint))
                {
                    orCreatePoint = GetOrCreatePoint(_rng.NextInt(0, 7), 1);
                }
            }
            startMapPoints.Add(orCreatePoint);
            PathGenerate(orCreatePoint);
        }
        ForEachInRow(_grid, GetRowCount() - 1, x => x.AddChildPoint(BossMapPoint));
        if (SecondBossMapPoint != null)
        {
            BossMapPoint.AddChildPoint(SecondBossMapPoint);
        }
        ForEachInRow(_grid, 1, x => StartingMapPoint.AddChildPoint(x));
    }

    private static void ForEachInRow(MapPoint?[,] grid, int rowIndex, Action<MapPoint> processor)
    {
        for (int i = 0; i < grid.GetLength(0); i++)
        {
            MapPoint? mapPoint = grid[i, rowIndex];
            if (mapPoint != null)
            {
                processor(mapPoint);
            }
        }
    }

    private void AssignPointTypes()
    {
        ForEachInRow(_grid, GetRowCount() - 1, p =>
        {
            p.PointType = MapPointType.RestSite;
            p.CanBeModified = false;
        });
        MapPointType fixedRowType = ShouldReplaceTreasureWithElites ? MapPointType.Elite : MapPointType.Treasure;
        ForEachInRow(_grid, GetRowCount() - 7, p =>
        {
            p.PointType = fixedRowType;
            p.CanBeModified = false;
        });
        ForEachInRow(_grid, 1, p =>
        {
            p.PointType = MapPointType.Monster;
            p.CanBeModified = false;
        });
        var list = new List<MapPointType>();
        MapPointTypeRules.EnqueueTypes(list, _pointTypeCounts);
        var pointTypesToBeAssigned = new Queue<MapPointType>(list);
        AssignRemainingTypesToRandomPoints(pointTypesToBeAssigned);
        foreach (MapPoint point in GetAllMapPoints().Where(x => x.PointType == MapPointType.Unassigned))
        {
            point.PointType = MapPointType.Monster;
        }
        BossMapPoint.PointType = MapPointType.Boss;
        StartingMapPoint.PointType = MapPointType.Ancient;
        if (SecondBossMapPoint != null)
        {
            SecondBossMapPoint.PointType = MapPointType.Boss;
        }
    }

    private void AssignRemainingTypesToRandomPoints(Queue<MapPointType> pointTypesToBeAssigned)
    {
        for (int i = 0; i < 3; i++)
        {
            if (pointTypesToBeAssigned.Count <= 0)
            {
                break;
            }
            List<MapPoint> points = GetAllMapPoints().Where(p => p.PointType == MapPointType.Unassigned).ToList();
            points.StableShuffle(_rng);
            foreach (MapPoint point in points)
            {
                if (pointTypesToBeAssigned.Count == 0)
                {
                    break;
                }
                point.PointType = GetNextValidPointType(pointTypesToBeAssigned, point);
            }
        }
    }

    private MapPointType GetNextValidPointType(Queue<MapPointType> pointTypesQueue, MapPoint mapPoint)
    {
        for (int i = 0; i < pointTypesQueue.Count; i++)
        {
            MapPointType mapPointType = pointTypesQueue.Dequeue();
            if (_pointTypeCounts.ShouldIgnoreMapPointRulesForMapPointType(mapPointType))
            {
                return mapPointType;
            }
            if (IsValidPointType(mapPointType, mapPoint))
            {
                return mapPointType;
            }
            pointTypesQueue.Enqueue(mapPointType);
        }
        return MapPointType.Unassigned;
    }

    public bool IsValidPointType(MapPointType pointType, MapPoint mapPoint)
        => MapPointTypeRules.IsValidPointType(_mapLength, pointType, mapPoint);
}
