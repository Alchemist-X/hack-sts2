using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// map_routes (appended 2026-11-04): the routing features of every map node, recomputed here only from the published
/// map_nodes columns (point_type, child_mask) — memoised recursion over the drawn edges, and an explicit enumeration of
/// every start-to-boss path for the start node — and compared with the encoder's table on every distinct map the
/// playouts reach (all characters, A0 and A10, acts 1-3).
/// </summary>
public static partial class VecEncoderTests
{
    private sealed record RouteRef(int ElitesMin, int ElitesMax, int Dist, HashSet<int> Reach);

    private const int RouteCols = Caps.MapCols;

    private static int NodeType(int[] nodes, int cell) => nodes[cell * Schema.MapNodeFields.Length];

    private static int ChildMask(int[] nodes, int cell) => nodes[cell * Schema.MapNodeFields.Length + 4];

    private static IEnumerable<int> RouteChildren(int[] nodes, int cell)
    {
        int row = cell / RouteCols;
        int mask = ChildMask(nodes, cell);
        for (int c = 0; c < RouteCols; c++)
        {
            int child = (row + 1) * RouteCols + c;
            if ((mask & (1 << c)) != 0 && row + 1 < Caps.MapRows && NodeType(nodes, child) != 0)
            {
                yield return child;
            }
        }
    }

    private static RouteRef RouteOf(int[] nodes, int cell, int boss, Dictionary<int, RouteRef> memo)
    {
        if (memo.TryGetValue(cell, out var known))
        {
            return known;
        }
        var reach = new HashSet<int> { cell };
        int dist = -1, min = int.MaxValue, max = -1;
        if (cell == boss)
        {
            dist = min = max = 0;
        }
        else
        {
            foreach (int child in RouteChildren(nodes, cell))
            {
                var r = RouteOf(nodes, child, boss, memo);
                reach.UnionWith(r.Reach);
                if (r.Dist >= 0)
                {
                    dist = dist < 0 ? r.Dist + 1 : Math.Min(dist, r.Dist + 1);
                    min = Math.Min(min, r.ElitesMin);
                    max = Math.Max(max, r.ElitesMax);
                }
            }
            if (dist >= 0 && NodeType(nodes, cell) == (int)MapPointType.Elite)
            {
                min++;
                max++;
            }
        }
        if (dist < 0)
        {
            min = max = -1;
        }
        return memo[cell] = new RouteRef(min, max, dist, reach);
    }

    /// <summary>Elite counts of every path from <paramref name="cell"/> to the boss (explicit enumeration).</summary>
    private static void EnumeratePaths(int[] nodes, int cell, int boss, int elites, List<int> totals, ref int budget)
    {
        if (--budget < 0)
        {
            return;
        }
        if (cell == boss)
        {
            totals.Add(elites);
            return;
        }
        int here = NodeType(nodes, cell) == (int)MapPointType.Elite ? 1 : 0;
        foreach (int child in RouteChildren(nodes, cell))
        {
            EnumeratePaths(nodes, child, boss, elites + here, totals, ref budget);
        }
    }

    public static void Test_VecEncoder_MapRoutesMatchTheDrawnGraph()
    {
        using var nb = new NativeBuffers(1);
        int w = Schema.MapRouteFields.Length;
        int nw = Schema.MapNodeFields.Length;
        var seen = new HashSet<string>();
        int maps = 0, nodesChecked = 0, enumerated = 0, unreachable = 0, maxElites = 0, acts3 = 0;
        Playouts(40, 2500, 97, heal: true, sim =>
        {
            if (sim.State.Map == null)
            {
                return true;
            }
            EncodeInto(sim, nb, out _);
            var nodes = nb.All<int>(Buf.MapNodes).ToArray();
            var routes = nb.All<int>(Buf.MapRoutes).ToArray();
            // the graph alone (point_type, child_mask) identifies a map; visited/current marks change along the way
            var key = string.Join(",", Enumerable.Range(0, Caps.MapRows * RouteCols).Select(k => $"{NodeType(nodes, k)}:{ChildMask(nodes, k)}"));
            if (!seen.Add(key))
            {
                // the same graph later in the act: the routes must not have changed
                return maps < 80;
            }
            maps++;
            if (sim.State.CurrentActIndex == 2)
            {
                acts3++;
            }
            int boss = Enumerable.Range(0, Caps.MapRows * RouteCols).Where(k => NodeType(nodes, k) == (int)MapPointType.Boss).Min();
            Assert.Equal(boss, RouteCellOf(sim.State.Map.BossMapPoint), "the first Boss cell is the act's boss");
            var memo = new Dictionary<int, RouteRef>();
            for (int k = 0; k < Caps.MapRows * RouteCols; k++)
            {
                var row = routes.AsSpan(k * w, w);
                if (NodeType(nodes, k) == 0)
                {
                    Assert.True(row.ToArray().All(v => v == 0), $"cell {k}: no node, zero row");
                    continue;
                }
                var r = RouteOf(nodes, k, boss, memo);
                int rests = r.Reach.Count(c => NodeType(nodes, c) == (int)MapPointType.RestSite);
                int shops = r.Reach.Count(c => NodeType(nodes, c) == (int)MapPointType.Shop);
                Assert.Equal(r.ElitesMin, row[MrCol.ElitesMin], $"map {maps} cell {k} elites_min");
                Assert.Equal(r.ElitesMax, row[MrCol.ElitesMax], $"map {maps} cell {k} elites_max");
                Assert.Equal(rests, row[MrCol.Rests], $"map {maps} cell {k} rests");
                Assert.Equal(shops, row[MrCol.Shops], $"map {maps} cell {k} shops");
                Assert.Equal(r.Dist, row[MrCol.BossDist], $"map {maps} cell {k} boss_dist");
                if (r.Dist >= 0)
                {
                    Assert.Equal(boss / RouteCols - k / RouteCols, r.Dist, "one row per move");
                }
                else
                {
                    unreachable++;
                }
                maxElites = Math.Max(maxElites, r.ElitesMax);
                nodesChecked++;
            }
            // every start-to-boss path, enumerated: min / max elites of the start node
            int start = RouteCellOf(sim.State.Map.StartingMapPoint);
            var totals = new List<int>();
            int budget = 2_000_000;
            EnumeratePaths(nodes, start, boss, 0, totals, ref budget);
            Assert.True(budget >= 0, "path enumeration within budget");
            Assert.True(totals.Count > 0, "the boss is reachable from the start");
            Assert.Equal(totals.Min(), routes[start * w + MrCol.ElitesMin], $"map {maps}: start elites_min over {totals.Count} paths");
            Assert.Equal(totals.Max(), routes[start * w + MrCol.ElitesMax], $"map {maps}: start elites_max over {totals.Count} paths");
            enumerated += totals.Count;
            Assert.Equal((int)MapPointType.Ancient, nodes[start * nw], "the start cell is the act's Ancient point");
            return maps < 80;
        });
        Assert.True(maps >= 30 && acts3 > 0, $"distinct maps {maps} (act 3: {acts3})");
        Console.WriteLine($"     map routes: {maps} distinct maps ({acts3} in act 3), {nodesChecked} nodes, {unreachable} without a path to the boss, " +
                          $"{enumerated} start-to-boss paths enumerated, max elites on a path {maxElites}");
    }

    private static int RouteCellOf(MapPoint pt) => pt.coord.row * RouteCols + pt.coord.col;
}
