namespace Sts2Sim.Core.Run;

public readonly record struct MapCoord(int col, int row) : IComparable<MapCoord>
{
    public int CompareTo(MapCoord other) => (col, row).CompareTo((other.col, other.row));

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteInt32(col);
        w.WriteInt32(row);
    }

    public override string ToString() => $"MapCoord ({col}, {row})";
}
