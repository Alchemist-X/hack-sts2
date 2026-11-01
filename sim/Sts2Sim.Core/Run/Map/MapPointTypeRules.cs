namespace Sts2Sim.Core.Run;

internal static class MapPointTypeRules
{
    private static readonly HashSet<MapPointType> LowerMapPointRestrictions = new() { MapPointType.RestSite, MapPointType.Elite };

    private static readonly HashSet<MapPointType> UpperMapPointRestrictions = new() { MapPointType.RestSite };

    private static readonly HashSet<MapPointType> ParentMapPointRestrictions = new() { MapPointType.Elite, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop };

    private static readonly HashSet<MapPointType> ChildMapPointRestrictions = new() { MapPointType.Elite, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop };

    private static readonly HashSet<MapPointType> SiblingPointTypeRestrictions = new() { MapPointType.RestSite, MapPointType.Monster, MapPointType.Unknown, MapPointType.Elite, MapPointType.Shop };

    public static bool IsValidPointType(int mapLength, MapPointType pointType, MapPoint mapPoint)
    {
        if (!IsValidForUpper(mapLength, pointType, mapPoint))
        {
            return false;
        }
        if (!IsValidForLower(pointType, mapPoint))
        {
            return false;
        }
        if (!IsValidWithParents(pointType, mapPoint))
        {
            return false;
        }
        if (!IsValidWithChildren(pointType, mapPoint))
        {
            return false;
        }
        if (!IsValidWithSiblings(pointType, mapPoint))
        {
            return false;
        }
        return true;
    }

    private static bool IsValidForLower(MapPointType pointType, MapPoint mapPoint)
    {
        if (mapPoint.coord.row < 6)
        {
            return !LowerMapPointRestrictions.Contains(pointType);
        }
        return true;
    }

    private static bool IsValidForUpper(int mapLength, MapPointType pointType, MapPoint mapPoint)
    {
        if (mapPoint.coord.row >= mapLength - 3)
        {
            return !UpperMapPointRestrictions.Contains(pointType);
        }
        return true;
    }

    private static bool IsValidWithParents(MapPointType pointType, MapPoint mapPoint)
    {
        if (ParentMapPointRestrictions.Contains(pointType))
        {
            return !mapPoint.parents.Concat(mapPoint.Children).Any(p => pointType == p.PointType);
        }
        return true;
    }

    private static bool IsValidWithChildren(MapPointType pointType, MapPoint mapPoint)
    {
        if (ChildMapPointRestrictions.Contains(pointType))
        {
            return !mapPoint.Children.Any(p => pointType == p.PointType);
        }
        return true;
    }

    private static bool IsValidWithSiblings(MapPointType pointType, MapPoint mapPoint)
    {
        if (SiblingPointTypeRestrictions.Contains(pointType))
        {
            return !GetSiblings(mapPoint).Any(p => pointType == p.PointType);
        }
        return true;
    }

    private static IEnumerable<MapPoint> GetSiblings(MapPoint mapPoint)
        => mapPoint.parents.SelectMany(x => x.Children).Where(x => !ReferenceEquals(x, mapPoint));

    public static void EnqueueTypes(List<MapPointType> list, MapPointTypeCounts counts)
    {
        for (int i = 0; i < counts.NumOfRests; i++)
        {
            list.Add(MapPointType.RestSite);
        }
        for (int i = 0; i < counts.NumOfShops; i++)
        {
            list.Add(MapPointType.Shop);
        }
        for (int i = 0; i < counts.NumOfElites; i++)
        {
            list.Add(MapPointType.Elite);
        }
        for (int i = 0; i < counts.NumOfUnknowns; i++)
        {
            list.Add(MapPointType.Unknown);
        }
    }
}
