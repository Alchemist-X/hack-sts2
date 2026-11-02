using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class MapTests
{
    private const string SaveSeed = "G1YFC196B2H2";

    private const string UnderdocksA8SavedMap = """
        7x16
        0,13 1 1 > 0,14
        0,14 5 1 > 1,15
        1,11 4 1 > 1,12
        1,12 6 1 > 0,13
        1,13 5 1 > 0,14 2,14
        1,15 4 1 > 3,16
        2,2 5 1 > 2,3
        2,3 5 1 > 3,4
        2,5 5 1 > 2,6
        2,6 6 1 > 3,7
        2,10 5 1 > 2,11 1,11
        2,11 5 1 > 2,12 1,12
        2,12 4 1 > 3,13 1,13
        2,14 6 1 > 2,15
        2,15 4 1 > 3,16
        3,1 5 1 > 4,2 2,2
        3,4 2 1 > 4,5 2,5
        3,7 4 1 > 4,8 3,8
        3,8 1 1 > 3,9
        3,9 3 1 > 3,10 2,10
        3,10 4 1 > 3,11 2,11
        3,11 6 1 > 2,12
        3,13 6 1 > 4,14
        4,2 5 1 > 4,3 5,3
        4,3 1 1 > 3,4 4,4
        4,4 5 1 > 4,5
        4,5 1 1 > 5,6 4,6
        4,6 6 1 > 3,7
        4,7 4 1 > 4,8
        4,8 5 1 > 3,9
        4,10 1 1 > 3,11
        4,14 1 1 > 4,15
        4,15 4 1 > 3,16
        5,3 2 1 > 4,4
        5,6 5 1 > 4,7 5,7
        5,7 1 1 > 6,8
        5,9 3 1 > 4,10 6,10
        5,14 2 1 > 4,15
        6,1 5 1 > 6,2
        6,2 5 1 > 6,3
        6,3 1 1 > 6,4
        6,4 5 1 > 6,5
        6,5 5 1 > 6,6 5,6
        6,6 1 1 > 6,7
        6,7 6 1 > 6,8
        6,8 1 1 > 5,9
        6,10 4 1 > 6,11
        6,11 6 1 > 6,12
        6,12 1 1 > 6,13
        6,13 1 1 > 5,14
        boss 3,16 7 1 >
        start 3,0 8 1 > 3,1 6,1
        start_coords 6,1 3,1
        """;

    private static StandardActMap BuildUnderdocks(ulong runSeed, int actIndex, int ascension, bool hasSecondBoss = false)
    {
        var rng = new Rng(runSeed, $"act_{actIndex + 1}_map");
        int rests = rng.NextGaussianInt(7, 1, 6, 7);
        int unknowns = MapPointTypeCounts.StandardRandomUnknownCount(rng);
        var counts = StandardActMap.WithAscensionElites(new MapPointTypeCounts(unknowns, rests), ascension);
        return new StandardActMap(rng, 15, counts, shouldReplaceTreasureWithElites: false, hasSecondBoss: hasSecondBoss);
    }

    private static byte[] Canonical(ActMap map)
    {
        var w = new CanonicalWriter();
        map.WriteCanonical(w);
        return w.ToArray();
    }

    public static void Test_Map_StandardActMap_MatchesSavedUnderdocksA8Map()
    {
        ulong seed = new RunRngSet(SaveSeed).Seed;
        StandardActMap map = BuildUnderdocks(seed, 0, 8);
        string actual = SerializableActMap.FromActMap(map).ToCompactString(canModifyAfterJsonRoundTrip: true);
        Assert.Equal(UnderdocksA8SavedMap.Replace("\r\n", "\n") + "\n", actual, "sim map equals the game's saved_map incl. HashSet child order");
    }

    public static void Test_Map_EliteCountsFollowAscension()
    {
        Assert.Equal(5, MapPointTypeCounts.EliteCountFor(0));
        Assert.Equal(8, MapPointTypeCounts.EliteCountFor(1));
        Assert.Equal(8, MapPointTypeCounts.EliteCountFor(10));
    }

    private const ulong RegressionDigest = 0x54C109DB29479375UL;

    public static void Test_Map_GenerationIsDeterministicAcrossSeeds()
    {
        var all = new CanonicalWriter();
        for (ulong s = 1; s <= 12; s++)
        {
            for (int act = 0; act < 3; act++)
            {
                StandardActMap a = BuildUnderdocks(s * 7919UL, act, (int)(s % 11), hasSecondBoss: act == 2);
                StandardActMap b = BuildUnderdocks(s * 7919UL, act, (int)(s % 11), hasSecondBoss: act == 2);
                Assert.SequenceEqual(Canonical(a), Canonical(b), $"seed {s} act {act}");
                a.WriteCanonical(all);
                Assert.Equal(MapPointType.Ancient, a.StartingMapPoint.PointType);
                Assert.Equal(MapPointType.Boss, a.BossMapPoint.PointType);
                Assert.Equal(act == 2, a.SecondBossMapPoint != null);
                foreach (MapPoint p in a.GetAllMapPoints())
                {
                    Assert.True(p.PointType != MapPointType.Unassigned, $"seed {s}: {p} assigned");
                    Assert.Equal(p, a.GetPoint(p.coord), "grid index equals coord after post-processing");
                    foreach (MapPoint c in p.Children)
                    {
                        Assert.True(Math.Abs(c.coord.col - p.coord.col) <= 1 || c == a.BossMapPoint, $"seed {s}: {p}->{c} adjacent");
                    }
                }
                foreach (MapPoint p in a.GetPointsInRow(a.GetRowCount() - 1))
                {
                    Assert.Equal(MapPointType.RestSite, p.PointType);
                }
            }
        }
        Assert.Equal($"0x{RegressionDigest:X16}", $"0x{DeterministicHash.XxHash64(all.WrittenSpan):X16}", "seed-based regression pin");
    }

    public static void Test_Map_SavedActMapRoundTripsTheProjection()
    {
        StandardActMap map = BuildUnderdocks(424242UL, 2, 10, hasSecondBoss: true);
        SerializableActMap saved = SerializableActMap.FromActMap(map);
        var loaded = new SavedActMap(saved);
        Assert.Equal(saved.ToCompactString(), SerializableActMap.FromActMap(loaded).ToCompactString());
        Assert.SequenceEqual(Canonical(map), Canonical(loaded));
    }

    public static void Test_Map_QuestOverlayIsCopyOnWrite()
    {
        StandardActMap map = BuildUnderdocks(99UL, 0, 0);
        var coord = map.GetAllMapPoints().First().coord;
        ActMap marked = map.WithQuestAdded(coord, "SPOILS_MAP#1");
        Assert.Equal(0, map.GetQuests(coord).Count, "original untouched");
        Assert.SequenceEqual(new[] { "SPOILS_MAP#1" }, marked.GetQuests(coord));
        Assert.True(ReferenceEquals(map.BossMapPoint, marked.BossMapPoint), "topology shared");
        Assert.NotEqual(Convert.ToHexString(Canonical(map)), Convert.ToHexString(Canonical(marked)));
        ActMap cleared = marked.WithQuestRemoved(coord, "SPOILS_MAP#1");
        Assert.SequenceEqual(Canonical(map), Canonical(cleared));
    }

    public static void Test_Map_GoldenPathIsOneColumn()
    {
        var map = new GoldenPathActMap(isMultiplayer: false);
        Assert.Equal(17, map.GetRowCount());
        var types = map.GetAllMapPoints().Select(p => p.PointType).ToArray();
        Assert.SequenceEqual(new[]
        {
            MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite,
            MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Treasure,
            MapPointType.Unknown, MapPointType.Treasure, MapPointType.Unknown, MapPointType.Shop,
            MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite,
        }, types);
        Assert.Equal(15, new GoldenPathActMap(isMultiplayer: true).GetAllMapPoints().Count());
    }

    public static void Test_Map_SpoilsMapFunnelsThroughTheTreasure()
    {
        for (ulong s = 1; s <= 20; s++)
        {
            var rng = new Rng(s, "spoils_map");
            int rests = rng.NextGaussianInt(7, 1, 6, 7);
            int unknowns = MapPointTypeCounts.StandardRandomUnknownCount(rng);
            var map = new SpoilsActMap(rng, 15, new MapPointTypeCounts(unknowns, rests));
            int treasureRow = map.GetRowCount() - 7;
            var row = map.GetPointsInRow(treasureRow).ToList();
            Assert.Equal(1, row.Count, $"seed {s}: one point in the treasure row");
            Assert.Equal(new MapCoord(3, treasureRow), row[0].coord);
            Assert.Equal(MapPointType.Treasure, row[0].PointType);
            foreach (List<MapPoint> path in MapPathPruning.FindAllPaths(map.StartingMapPoint))
            {
                Assert.True(path.Contains(row[0]), $"seed {s}: every path passes the treasure");
            }
        }
    }

    public static void Test_Map_LegalMapPointsFollowTheMapScreen()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(5UL), 0);
        StandardActMap map = BuildUnderdocks(5UL, 0, 0, hasSecondBoss: true);
        rs.Map = map;
        Assert.SequenceEqual(new[] { map.StartingMapPoint }, MapTravel.GetLegalMapPoints(rs));
        rs.VisitedMapCoords.Add(map.StartingMapPoint.coord);
        List<MapPoint> first = MapTravel.GetLegalMapPoints(rs);
        Assert.SequenceEqual(map.GetPointsInRow(1), first, "start → every row-1 point, column ascending");
        MapPoint branch = map.GetAllMapPoints().First(p => p.Children.Count > 1 && p.coord.row < map.GetRowCount() - 1);
        rs.VisitedMapCoords.Add(branch.coord);
        Assert.SequenceEqual(branch.Children.OrderBy(c => c.coord.col), MapTravel.GetLegalMapPoints(rs));
        rs.VisitedMapCoords.Add(map.GetPointsInRow(map.GetRowCount() - 1).First().coord);
        Assert.SequenceEqual(new[] { map.BossMapPoint }, MapTravel.GetLegalMapPoints(rs));
        rs.VisitedMapCoords.Add(map.BossMapPoint.coord);
        Assert.SequenceEqual(new[] { map.SecondBossMapPoint! }, MapTravel.GetLegalMapPoints(rs));
    }

    public static void Test_Map_CreateForUsesTheActMapStream()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(SaveSeed), 8);
        var underdocks = ActRegistry.Get("UNDERDOCKS");
        rs.Acts.Add(new ActInstance(underdocks));
        StandardActMap map = StandardActMap.CreateFor(rs, replaceTreasureWithElites: false);
        Assert.Equal(UnderdocksA8SavedMap.Replace("\r\n", "\n") + "\n", SerializableActMap.FromActMap(map).ToCompactString(canModifyAfterJsonRoundTrip: true));
    }
}
