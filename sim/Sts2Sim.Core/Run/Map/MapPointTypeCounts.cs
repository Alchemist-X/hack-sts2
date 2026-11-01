namespace Sts2Sim.Core.Run;

public sealed class MapPointTypeCounts
{
    public const int BaseEliteCount = 5;

    public MapPointTypeCounts(int unknownCount, int restCount)
    {
        NumOfUnknowns = unknownCount;
        NumOfRests = restCount;
    }

    public MapPointTypeCounts(ActMap existingMap)
    {
        MapPoint[] array = existingMap.GetAllMapPoints().ToArray();
        NumOfUnknowns = array.Count(p => p.PointType == MapPointType.Unknown);
        NumOfRests = array.Count(p => p.PointType == MapPointType.RestSite);
    }

    public HashSet<MapPointType> PointTypesThatIgnoreRules { get; init; } = new();

    public int NumOfElites { get; set; } = BaseEliteCount;

    public int NumOfShops { get; set; } = 3;

    public int NumOfUnknowns { get; set; }

    public int NumOfRests { get; set; }

    public bool ShouldIgnoreMapPointRulesForMapPointType(MapPointType pointType) => PointTypesThatIgnoreRules.Contains(pointType);

    public static int EliteCountFor(int ascension)
        => (int)Math.Round((double)(5f * (ascension >= (int)AscensionLevel.SwarmingElites ? 1.6f : 1f)));

    public static int StandardRandomUnknownCount(Rng rng) => rng.NextGaussianInt(12, 1, 10, 14);
}
