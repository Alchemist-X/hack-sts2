namespace Sts2Sim.Core.Run;

public sealed class StandardActMap : ActMap
{
    public const int maxElites = 15;

    public StandardActMap(Rng mapRng, ActModel actModel, bool isMultiplayer, bool shouldReplaceTreasureWithElites, bool hasSecondBoss = false, MapPointTypeCounts? mapPointTypeCountsOverride = null, bool enablePruning = true)
    {
        throw new NotImplementedException("W2 R2: StandardActMap..ctor");
    }

    public override MapPoint BossMapPoint => throw new NotImplementedException("W2 R2: StandardActMap.BossMapPoint");

    public override MapPoint StartingMapPoint => throw new NotImplementedException("W2 R2: StandardActMap.StartingMapPoint");

    protected override MapPoint?[,] Grid => throw new NotImplementedException("W2 R2: StandardActMap.Grid");

    public static StandardActMap CreateFor(RunState runState, bool replaceTreasureWithElites) => throw new NotImplementedException("W2 R2: StandardActMap.CreateFor");
}
