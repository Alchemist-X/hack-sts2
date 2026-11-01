namespace Sts2Sim.Core.Run;

public sealed class MapPointTypeCounts
{
    public MapPointTypeCounts(int unknownCount, int restCount)
    {
        NumOfUnknowns = unknownCount;
        NumOfRests = restCount;
    }

    public int NumOfElites { get; set; }

    public int NumOfShops { get; set; }

    public int NumOfUnknowns { get; set; }

    public int NumOfRests { get; set; }

    public static int StandardRandomUnknownCount(Rng rng) => throw new NotImplementedException("W2 R2: MapPointTypeCounts.StandardRandomUnknownCount");
}
