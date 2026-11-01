namespace Sts2Sim.Core.Run;

public sealed class SpoilsActMap : ActMap
{
    private const int _mapWidth = 7;

    private const int _pathCount = 7;

    private readonly int _mapLength;

    private readonly Rng _rng;

    private readonly int _treasureRow;

    private readonly MapPointTypeCounts _pointTypeCounts;

    private readonly MapPoint?[,] _grid;

    public SpoilsActMap(RunState runState, MapPointTypeCounts? mapPointTypeCountsOverride = null)
        : this(CreateArgs(runState, mapPointTypeCountsOverride))
    {
    }

    public SpoilsActMap(Rng rng, int numberOfRooms, MapPointTypeCounts pointTypeCounts)
        : this((rng, numberOfRooms, pointTypeCounts))
    {
    }

    private SpoilsActMap((Rng Rng, int NumberOfRooms, MapPointTypeCounts Counts) args)
    {
        _mapLength = args.NumberOfRooms + 1;
        _rng = args.Rng;
        _pointTypeCounts = args.Counts;
        _grid = new MapPoint?[_mapWidth, _mapLength];
        _treasureRow = GetRowCount() - 7;
        BossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount());
        StartingMapPoint = new MapPoint(GetColumnCount() / 2, 0);
        GenerateHourglassMap();
        AssignPointTypes();
        MapPathPruning.PruneAndRepair(_grid, startMapPoints, this, _pointTypeCounts, _rng, IsValidPointType);
    }

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    protected override MapPoint?[,] Grid => _grid;

    private static (Rng, int, MapPointTypeCounts) CreateArgs(RunState runState, MapPointTypeCounts? mapPointTypeCountsOverride)
    {
        ActModel act = runState.Act.Def;
        bool isMultiplayer = runState.Players.Count > 1;
        int rooms = act.GetNumberOfRooms(isMultiplayer);
        var rng = new Rng(runState.Rng.Seed, "spoils_map");
        MapPointTypeCounts counts = mapPointTypeCountsOverride ?? StandardActMap.WithAscensionElites(act.GetMapPointTypes(rng), runState.Ascension);
        return (rng, rooms, counts);
    }

    private MapPoint GetOrCreatePoint(int col, int row)
    {
        if (col >= 0 && col < GetColumnCount() && row >= 0 && row < GetRowCount())
        {
            MapPoint? mapPoint = _grid[col, row];
            if (mapPoint != null)
            {
                return mapPoint;
            }
        }
        var created = new MapPoint(col, row);
        _grid[col, row] = created;
        return created;
    }

    private void GenerateHourglassMap()
    {
        int col = GetColumnCount() / 2;
        if (_treasureRow <= 0 || _treasureRow >= GetRowCount())
        {
            throw new InvalidOperationException("Treasure row is out of bounds for SpoilsActMap");
        }
        for (int i = 0; i < _pathCount; i++)
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
        MapPoint treasure = GetOrCreatePoint(col, _treasureRow);
        treasure.PointType = MapPointType.Treasure;
        treasure.CanBeModified = false;
        foreach (MapPoint point in GetPointsInRow(_treasureRow).ToList())
        {
            if (point != treasure)
            {
                RedirectToTreasure(point, treasure);
            }
        }
        ConnectRowToBoss();
        ConnectRowToStart();
    }

    private void PathGenerate(MapPoint startingPoint)
    {
        MapPoint mapPoint = startingPoint;
        while (mapPoint.coord.row < _mapLength - 1)
        {
            MapCoord next = GenerateNextCoord(mapPoint);
            MapPoint orCreatePoint = GetOrCreatePoint(next.col, next.row);
            mapPoint.AddChildPoint(orCreatePoint);
            mapPoint = orCreatePoint;
        }
    }

    private MapCoord GenerateNextCoord(MapPoint current)
    {
        int row = current.coord.row + 1;
        (int minCol, int maxCol) = GetAllowedColumnsForRow(row);
        int center = GetColumnCount() / 2;
        var directions = new List<int> { -1, 0, 1 };
        int distance = _treasureRow - current.coord.row;
        if (distance > 3)
        {
            directions.StableShuffle(_rng);
        }
        else if (distance > 0)
        {
            directions = BuildCenteredPriorityList(current.coord.col, center);
        }
        else
        {
            directions.StableShuffle(_rng);
        }
        foreach (int direction in directions)
        {
            int nextColumn = GetNextColumn(current.coord.col, direction);
            if (nextColumn < minCol || nextColumn > maxCol || HasInvalidCrossover(current, nextColumn))
            {
                continue;
            }
            MapPoint? point = GetPoint(nextColumn, row);
            if ((point == null || point.parents.Contains(current) || point.parents.Count < 3) && (current == StartingMapPoint || current.Children.Count < 3 || (point != null && current.Children.Contains(point))))
            {
                if (Math.Abs(nextColumn - current.coord.col) > 1)
                {
                    throw new InvalidOperationException($"Invalid step from ({current.coord.col}, {current.coord.row}) to column {nextColumn}");
                }
                return new MapCoord(nextColumn, row);
            }
        }
        int fallback = Math.Clamp(center, minCol, maxCol);
        if (Math.Abs(fallback - current.coord.col) > 1)
        {
            fallback = Math.Clamp(current.coord.col + Math.Sign(fallback - current.coord.col), minCol, maxCol);
        }
        if (HasInvalidCrossover(current, fallback))
        {
            fallback = Math.Clamp(current.coord.col, minCol, maxCol);
        }
        if (Math.Abs(fallback - current.coord.col) > 1)
        {
            throw new InvalidOperationException($"Fallback step from ({current.coord.col}, {current.coord.row}) to column {fallback} exceeds adjacency");
        }
        return new MapCoord(fallback, row);
    }

    private static List<int> BuildCenteredPriorityList(int currentCol, int centerCol)
    {
        var list = new List<int>();
        int toward = Math.Sign(centerCol - currentCol);
        if (toward != 0)
        {
            list.Add(toward);
        }
        list.Add(0);
        int away = -toward;
        if (toward != 0)
        {
            list.Add(away);
        }
        if (!list.Contains(-1))
        {
            list.Add(-1);
        }
        if (!list.Contains(1))
        {
            list.Add(1);
        }
        return list;
    }

    private static int GetNextColumn(int currentCol, int direction) => direction switch
    {
        -1 => Math.Max(0, currentCol - 1),
        0 => currentCol,
        1 => Math.Min(6, currentCol + 1),
        _ => currentCol,
    };

    private (int, int) GetAllowedColumnsForRow(int row)
    {
        int center = GetColumnCount() / 2;
        int fromTreasure = Math.Abs(row - _treasureRow);
        int fromTop = _mapLength - 1 - row;
        int topLimit = Math.Min(center, Math.Max(0, fromTop) + 1);
        int halfWidth = Math.Min(center, Math.Min(fromTreasure, topLimit));
        int min = Math.Max(0, center - halfWidth);
        int max = Math.Min(6, center + halfWidth);
        return (min, max);
    }

    private bool HasInvalidCrossover(MapPoint current, int targetCol)
    {
        int num = targetCol - current.coord.col;
        if (num == 0)
        {
            return false;
        }
        MapPoint? point = GetPoint(targetCol, current.coord.row);
        if (point == null)
        {
            return false;
        }
        foreach (MapPoint child in point.Children)
        {
            if (child.coord.col - point.coord.col == -num)
            {
                return true;
            }
        }
        return false;
    }

    private void RedirectToTreasure(MapPoint strayNode, MapPoint treasure)
    {
        foreach (MapPoint parent in strayNode.parents.ToList())
        {
            parent.RemoveChildPoint(strayNode);
            parent.AddChildPoint(treasure);
        }
        foreach (MapPoint child in strayNode.Children.ToList())
        {
            strayNode.RemoveChildPoint(child);
            treasure.AddChildPoint(child);
        }
        _grid[strayNode.coord.col, strayNode.coord.row] = null;
    }

    private void ConnectRowToBoss()
    {
        int row = GetRowCount() - 1;
        for (int i = 0; i < GetColumnCount(); i++)
        {
            MapPoint? mapPoint = _grid[i, row];
            if (mapPoint != null && !mapPoint.Children.Contains(BossMapPoint))
            {
                mapPoint.AddChildPoint(BossMapPoint);
            }
        }
    }

    private void ConnectRowToStart()
    {
        for (int i = 0; i < GetColumnCount(); i++)
        {
            MapPoint? mapPoint = _grid[i, 1];
            if (mapPoint != null && !StartingMapPoint.Children.Contains(mapPoint))
            {
                StartingMapPoint.AddChildPoint(mapPoint);
            }
        }
    }

    private void AssignPointTypes()
    {
        ForEachInRow(GetRowCount() - 1, p =>
        {
            p.PointType = MapPointType.RestSite;
            p.CanBeModified = false;
        });
        ForEachInRow(1, p =>
        {
            p.PointType = MapPointType.Monster;
            p.CanBeModified = false;
        });
        var list = new List<MapPointType>();
        MapPointTypeRules.EnqueueTypes(list, _pointTypeCounts);
        var pointTypesToBeAssigned = new Queue<MapPointType>(list);
        AssignRemainingTypesToRandomPoints(pointTypesToBeAssigned);
        foreach (MapPoint point in GetAllMapPoints().Where(p => p.PointType == MapPointType.Unassigned))
        {
            point.PointType = MapPointType.Monster;
        }
        BossMapPoint.PointType = MapPointType.Boss;
        StartingMapPoint.PointType = MapPointType.Ancient;
    }

    private void ForEachInRow(int rowIndex, Action<MapPoint> processor)
    {
        for (int i = 0; i < GetColumnCount(); i++)
        {
            MapPoint? mapPoint = _grid[i, rowIndex];
            if (mapPoint != null)
            {
                processor(mapPoint);
            }
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
            List<MapPoint> points = GetAllMapPoints()
                .Where(p => p != BossMapPoint && p != StartingMapPoint)
                .Where(p => p.PointType == MapPointType.Unassigned)
                .ToList();
            points.StableShuffle(_rng);
            foreach (MapPoint point in points)
            {
                if (pointTypesToBeAssigned.Count == 0)
                {
                    break;
                }
                MapPointType next = GetNextValidPointType(pointTypesToBeAssigned, point);
                if (next != MapPointType.Unassigned)
                {
                    point.PointType = next;
                }
            }
        }
    }

    private MapPointType GetNextValidPointType(Queue<MapPointType> pointTypesQueue, MapPoint mapPoint)
    {
        if (pointTypesQueue.Count == 0)
        {
            return MapPointType.Unassigned;
        }
        int count = pointTypesQueue.Count;
        for (int i = 0; i < count; i++)
        {
            MapPointType type = pointTypesQueue.Dequeue();
            if (_pointTypeCounts.ShouldIgnoreMapPointRulesForMapPointType(type) || IsValidPointType(type, mapPoint))
            {
                return type;
            }
            pointTypesQueue.Enqueue(type);
        }
        return MapPointType.Unassigned;
    }

    private bool IsValidPointType(MapPointType pointType, MapPoint mapPoint)
        => MapPointTypeRules.IsValidPointType(_mapLength, pointType, mapPoint);
}
