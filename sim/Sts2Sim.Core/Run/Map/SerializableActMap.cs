using System.Text;

namespace Sts2Sim.Core.Run;

public sealed class SerializableMapPoint
{
    public MapCoord Coord { get; set; }

    public MapPointType PointType { get; set; }

    public bool CanBeModified { get; set; } = true;

    public List<MapCoord>? ChildCoords { get; set; }

    public static SerializableMapPoint FromMapPoint(MapPoint point)
    {
        List<MapCoord>? children = null;
        if (point.Children.Count > 0)
        {
            children = new List<MapCoord>(point.Children.Count);
            foreach (MapPoint child in point.Children)
            {
                children.Add(child.coord);
            }
        }
        return new SerializableMapPoint
        {
            Coord = point.coord,
            PointType = point.PointType,
            CanBeModified = point.CanBeModified,
            ChildCoords = children,
        };
    }
}

public sealed class SerializableActMap
{
    public List<SerializableMapPoint> Points { get; set; } = new();

    public SerializableMapPoint BossPoint { get; set; } = new();

    public SerializableMapPoint? SecondBossPoint { get; set; }

    public SerializableMapPoint StartingPoint { get; set; } = new();

    public List<MapCoord>? StartMapPointCoords { get; set; }

    public int GridWidth { get; set; }

    public int GridHeight { get; set; }

    public static SerializableActMap FromActMap(ActMap map)
    {
        var points = new List<SerializableMapPoint>();
        foreach (MapPoint point in map.GetAllMapPoints())
        {
            points.Add(SerializableMapPoint.FromMapPoint(point));
        }
        List<MapCoord>? starts = null;
        if (map.startMapPoints.Count > 0)
        {
            starts = new List<MapCoord>();
            foreach (MapPoint start in map.startMapPoints)
            {
                starts.Add(start.coord);
            }
        }
        return new SerializableActMap
        {
            Points = points,
            BossPoint = SerializableMapPoint.FromMapPoint(map.BossMapPoint),
            SecondBossPoint = map.SecondBossMapPoint != null ? SerializableMapPoint.FromMapPoint(map.SecondBossMapPoint) : null,
            StartingPoint = SerializableMapPoint.FromMapPoint(map.StartingMapPoint),
            StartMapPointCoords = starts,
            GridWidth = map.GetColumnCount(),
            GridHeight = map.GetRowCount(),
        };
    }

    public string ToCompactString(bool canModifyAfterJsonRoundTrip = false)
    {
        var sb = new StringBuilder();
        sb.Append(GridWidth).Append('x').Append(GridHeight).Append('\n');
        foreach (SerializableMapPoint point in Points)
        {
            AppendPoint(sb, point, canModifyAfterJsonRoundTrip);
        }
        sb.Append("boss ");
        AppendPoint(sb, BossPoint, canModifyAfterJsonRoundTrip);
        if (SecondBossPoint != null)
        {
            sb.Append("second_boss ");
            AppendPoint(sb, SecondBossPoint, canModifyAfterJsonRoundTrip);
        }
        sb.Append("start ");
        AppendPoint(sb, StartingPoint, canModifyAfterJsonRoundTrip);
        sb.Append("start_coords");
        if (StartMapPointCoords != null)
        {
            foreach (MapCoord coord in StartMapPointCoords)
            {
                sb.Append(' ').Append(coord.col).Append(',').Append(coord.row);
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    private static void AppendPoint(StringBuilder sb, SerializableMapPoint point, bool canModifyAfterJsonRoundTrip)
    {
        sb.Append(point.Coord.col).Append(',').Append(point.Coord.row).Append(' ').Append((int)point.PointType).Append(' ').Append(point.CanBeModified || canModifyAfterJsonRoundTrip ? '1' : '0').Append(" >");
        if (point.ChildCoords != null)
        {
            foreach (MapCoord child in point.ChildCoords)
            {
                sb.Append(' ').Append(child.col).Append(',').Append(child.row);
            }
        }
        sb.Append('\n');
    }
}
