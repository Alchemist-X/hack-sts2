namespace Sts2Sim.Core.Run;

public sealed partial class MapPoint : IComparable<MapPoint>
{
    public readonly HashSet<MapPoint> parents;

    public MapCoord coord;

    public MapPoint(int col, int row)
    {
        coord = new MapCoord(col, row);
        parents = new HashSet<MapPoint>();
        Children = new HashSet<MapPoint>();
    }

    public bool CanBeModified { get; set; } = true;

    public MapPointType PointType { get; set; }

    public HashSet<MapPoint> Children { get; }

    public int CompareTo(MapPoint? other) => other == null ? 1 : coord.CompareTo(other.coord);

    public void AddChildPoint(MapPoint child)
    {
        Children.Add(child);
        child.parents.Add(this);
    }

    public void RemoveChildPoint(MapPoint child)
    {
        Children.Remove(child);
        child.parents.Remove(this);
    }

    internal void SetColumn(int col) => coord = new MapCoord(col, coord.row);

    public override string ToString() => $"Point[{coord.col},{coord.row}]";
}
