namespace Sts2Sim.Core.Run;

public abstract partial class ActMap : ICanonical
{
    public readonly HashSet<MapPoint> startMapPoints = new();

    public abstract MapPoint BossMapPoint { get; }

    public abstract MapPoint StartingMapPoint { get; }

    public virtual MapPoint? SecondBossMapPoint => null;

    protected abstract MapPoint?[,] Grid { get; }

    public int GetColumnCount() => Grid.GetLength(0);

    public int GetRowCount() => Grid.GetLength(1);

    public IEnumerable<MapPoint> GetAllMapPoints() => throw new NotImplementedException("W2 R2: ActMap.GetAllMapPoints");

    public IEnumerable<MapPoint> GetPointsInRow(int row) => throw new NotImplementedException("W2 R2: ActMap.GetPointsInRow");

    public virtual MapPoint? GetPoint(MapCoord coord) => GetPoint(coord.col, coord.row);

    public MapPoint? GetPoint(int col, int row) => throw new NotImplementedException("W2 R2: ActMap.GetPoint");

    public bool IsInMap(MapPoint mapPoint) => throw new NotImplementedException("W2 R2: ActMap.IsInMap");

    public bool HasPoint(MapCoord coord) => throw new NotImplementedException("W2 R2: ActMap.HasPoint");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R2: ActMap.WriteCanonical");
}
