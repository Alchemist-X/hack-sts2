using System.Text;

namespace Sts2Sim.Core.Run;

public static class MapPathPruning
{
    public static void PruneAndRepair(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, ActMap map, MapPointTypeCounts pointTypeCounts, Rng rng, Func<MapPointType, MapPoint, bool> isValidPointType)
    {
        for (int i = 0; i < 3; i++)
        {
            PruneDuplicateSegments(grid, startMapPoints, map.StartingMapPoint, rng);
            if (!RepairPrunedPointTypes(map, pointTypeCounts, rng, isValidPointType))
            {
                break;
            }
        }
    }

    public static bool RepairPrunedPointTypes(ActMap map, MapPointTypeCounts pointTypeCounts, Rng rng, Func<MapPointType, MapPoint, bool> isValidPointType)
    {
        bool flag = false;
        flag |= RepairPointType(map, MapPointType.Shop, pointTypeCounts.NumOfShops, rng, isValidPointType);
        flag |= RepairPointType(map, MapPointType.Elite, pointTypeCounts.NumOfElites, rng, isValidPointType);
        flag |= RepairPointType(map, MapPointType.RestSite, pointTypeCounts.NumOfRests, rng, isValidPointType);
        return flag | RepairPointType(map, MapPointType.Unknown, pointTypeCounts.NumOfUnknowns, rng, isValidPointType);
    }

    private static bool RepairPointType(ActMap map, MapPointType type, int targetCount, Rng rng, Func<MapPointType, MapPoint, bool> isValidPointType)
    {
        int existing = map.GetAllMapPoints().Count(p => p.PointType == type);
        int deficit = targetCount - existing;
        if (deficit <= 0)
        {
            return false;
        }
        bool result = false;
        List<MapPoint> candidates = map.GetAllMapPoints().Where(p => p.PointType == MapPointType.Monster && p.CanBeModified).ToList();
        candidates.StableShuffle(rng);
        foreach (MapPoint point in candidates)
        {
            if (deficit == 0)
            {
                break;
            }
            if (isValidPointType(type, point))
            {
                point.PointType = type;
                deficit--;
                result = true;
            }
        }
        return result;
    }

    public static void PruneDuplicateSegments(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, MapPoint startingMapPoint, Rng rng)
    {
        int num = 0;
        List<List<MapPoint[]>> matchingSegments = FindMatchingSegments(startingMapPoint);
        while (PrunePaths(grid, startMapPoints, matchingSegments, rng))
        {
            num++;
            if (num > 50)
            {
                throw new InvalidOperationException($"Unable to prune matching segments in {num} iterations");
            }
            matchingSegments = FindMatchingSegments(startingMapPoint);
        }
    }

    public static List<List<MapPoint[]>> FindMatchingSegments(MapPoint startingMapPoint)
    {
        List<List<MapPoint>> paths = FindAllPaths(startingMapPoint);
        var segments = new SortedDictionary<string, List<MapPoint[]>>(StringComparer.Ordinal);
        var keyBuilder = new StringBuilder();
        foreach (List<MapPoint> path in paths)
        {
            AddSegmentsToDictionary(path, segments, keyBuilder);
        }
        return GetDuplicateSegments(segments);
    }

    public static List<List<MapPoint>> FindAllPaths(MapPoint currentMapPoint)
        => FindAllPaths(currentMapPoint, new Dictionary<MapPoint, List<List<MapPoint>>>(ReferenceEqualityComparer.Instance));

    private static List<List<MapPoint>> FindAllPaths(MapPoint currentMapPoint, Dictionary<MapPoint, List<List<MapPoint>>> memo)
    {
        if (memo.TryGetValue(currentMapPoint, out List<List<MapPoint>>? cached))
        {
            return cached;
        }
        var result = new List<List<MapPoint>>();
        if (currentMapPoint.PointType == MapPointType.Boss)
        {
            result.Add(new List<MapPoint>(1) { currentMapPoint });
            memo[currentMapPoint] = result;
            return result;
        }
        foreach (MapPoint child in currentMapPoint.Children)
        {
            List<List<MapPoint>> childPaths = FindAllPaths(child, memo);
            foreach (List<MapPoint> childPath in childPaths)
            {
                var path = new List<MapPoint>(childPath.Count + 1) { currentMapPoint };
                path.AddRange(childPath);
                result.Add(path);
            }
        }
        memo[currentMapPoint] = result;
        return result;
    }

    private static void AddSegmentsToDictionary(List<MapPoint> path, SortedDictionary<string, List<MapPoint[]>> segments, StringBuilder keyBuilder)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            if (!IsValidSegmentStartMapPoint(path[i]))
            {
                continue;
            }
            for (int j = 2; j < path.Count - i; j++)
            {
                MapPoint endMapPoint = path[i + j];
                if (!IsValidSegmentEndMapPoint(endMapPoint))
                {
                    continue;
                }
                string key = GenerateSegmentKey(path, i, j + 1, keyBuilder);
                if (!segments.TryGetValue(key, out List<MapPoint[]>? list))
                {
                    segments[key] = new List<MapPoint[]>(1) { path.GetRange(i, j + 1).ToArray() };
                }
                else if (!AnyOverlappingSegments(list, path, i, j + 1))
                {
                    list.Add(path.GetRange(i, j + 1).ToArray());
                }
            }
        }
    }

    private static bool IsValidSegmentStartMapPoint(MapPoint startMapPoint)
    {
        if (startMapPoint.Children.Count <= 1)
        {
            return startMapPoint.coord.row == 0;
        }
        return true;
    }

    private static bool IsValidSegmentEndMapPoint(MapPoint endMapPoint) => endMapPoint.parents.Count >= 2;

    private static string GenerateSegmentKey(List<MapPoint> path, int start, int count, StringBuilder sb)
    {
        sb.Clear();
        MapPoint first = path[start];
        MapPoint last = path[start + count - 1];
        if (first.coord.row == 0)
        {
            sb.Append(first.coord.row).Append('-').Append(last.coord.col).Append(',').Append(last.coord.row).Append('-');
        }
        else
        {
            sb.Append(first.coord.col).Append(',').Append(first.coord.row).Append('-').Append(last.coord.col).Append(',').Append(last.coord.row).Append('-');
        }
        for (int k = 0; k < count; k++)
        {
            if (k > 0)
            {
                sb.Append(',');
            }
            sb.Append((int)path[start + k].PointType);
        }
        return sb.ToString();
    }

    private static bool AnyOverlappingSegments(List<MapPoint[]> existingSegments, List<MapPoint> path, int start, int count)
    {
        foreach (MapPoint[] existing in existingSegments)
        {
            if (OverlappingSegment(existing, path, start, count))
            {
                return true;
            }
        }
        return false;
    }

    private static bool OverlappingSegment(MapPoint[] a, List<MapPoint> path, int start, int count)
    {
        if (a.Length < 3 || count < 3)
        {
            return false;
        }
        for (int i = 1; i <= a.Length - 2; i++)
        {
            if (ReferenceEquals(a[i], path[start + i]))
            {
                return true;
            }
        }
        return false;
    }

    private static List<List<MapPoint[]>> GetDuplicateSegments(SortedDictionary<string, List<MapPoint[]>> segments)
        => segments.Values.Where(list => list.Count > 1).ToList();

    private static bool PrunePaths(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, IEnumerable<List<MapPoint[]>> matchingSegments, Rng rng)
    {
        foreach (List<MapPoint[]> group in matchingSegments)
        {
            group.UnstableShuffle(rng);
            if (PruneAllButLast(grid, startMapPoints, group) != 0)
            {
                return true;
            }
            if (BreakAParentChildRelationshipInAnySegment(group))
            {
                return true;
            }
        }
        return false;
    }

    private static int PruneAllButLast(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, IReadOnlyList<MapPoint[]> matches)
    {
        int num = 0;
        foreach (MapPoint[] segment in matches)
        {
            if (num == matches.Count - 1)
            {
                return num;
            }
            if (PruneSegment(grid, startMapPoints, segment))
            {
                num++;
            }
        }
        return num;
    }

    private static bool PruneSegment(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, MapPoint[] segment)
    {
        bool result = false;
        for (int i = 0; i < segment.Length - 1; i++)
        {
            MapPoint mapPoint = segment[i];
            if (!IsInMap(grid, mapPoint))
            {
                return true;
            }
            if (mapPoint.Children.Count > 1 || mapPoint.parents.Count > 1 || mapPoint.parents.Any(n => n.Children.Count == 1 && !IsRemoved(grid, n)))
            {
                continue;
            }
            MapPoint[] rest = segment.Skip(i).ToArray();
            if (rest.Any(n => n.Children.Count > 1 && n.parents.Count == 1))
            {
                continue;
            }
            if (segment[segment.Length - 1].parents.Count == 1)
            {
                return false;
            }
            if (!mapPoint.Children.Where(c => !segment.Contains(c)).Any(c => c.parents.Count == 1))
            {
                RemovePoint(grid, startMapPoints, mapPoint);
                result = true;
            }
        }
        return result;
    }

    private static void RemovePoint(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, MapPoint mapPoint)
    {
        grid[mapPoint.coord.col, mapPoint.coord.row] = null;
        startMapPoints.Remove(mapPoint);
        foreach (MapPoint child in mapPoint.Children.ToList())
        {
            mapPoint.RemoveChildPoint(child);
        }
        foreach (MapPoint parent in mapPoint.parents.ToList())
        {
            parent.RemoveChildPoint(mapPoint);
        }
    }

    private static bool IsInMap(MapPoint?[,] grid, MapPoint mapPoint)
    {
        if (grid[mapPoint.coord.col, mapPoint.coord.row] == null && mapPoint.PointType != MapPointType.Ancient)
        {
            return mapPoint.PointType == MapPointType.Boss;
        }
        return true;
    }

    private static bool IsRemoved(MapPoint?[,] grid, MapPoint mapPoint) => grid[mapPoint.coord.col, mapPoint.coord.row] == null;

    private static bool BreakAParentChildRelationshipInAnySegment(List<MapPoint[]> matches)
    {
        foreach (MapPoint[] segment in matches)
        {
            if (BreakAParentChildRelationshipInSegment(segment))
            {
                return true;
            }
        }
        return false;
    }

    private static bool BreakAParentChildRelationshipInSegment(MapPoint[] segment)
    {
        bool result = false;
        for (int i = 0; i < segment.Length - 1; i++)
        {
            MapPoint mapPoint = segment[i];
            if (mapPoint.Children.Count >= 2)
            {
                MapPoint next = segment[i + 1];
                if (next.parents.Count != 1)
                {
                    mapPoint.RemoveChildPoint(next);
                    result = true;
                }
            }
        }
        return result;
    }
}
