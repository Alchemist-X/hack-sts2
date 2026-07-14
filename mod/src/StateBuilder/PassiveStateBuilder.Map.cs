// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs (map section).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations (game v0.99.1): read-only; boss identity guarded as upstream.

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static Dictionary<string, object?> BuildMapState(RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var map = runState.Map;
        var visitedCoords = runState.VisitedMapCoords;

        // Current position
        if (visitedCoords.Count > 0)
        {
            var cur = visitedCoords[visitedCoords.Count - 1];
            state["current_position"] = new Dictionary<string, object?>
            {
                ["col"] = cur.col, ["row"] = cur.row,
                ["type"] = map.GetPoint(cur)?.PointType.ToString()
            };
        }

        // Visited path
        var visited = new List<Dictionary<string, object?>>();
        foreach (var coord in visitedCoords)
        {
            visited.Add(new Dictionary<string, object?>
            {
                ["col"] = coord.col, ["row"] = coord.row,
                ["type"] = map.GetPoint(coord)?.PointType.ToString()
            });
        }
        state["visited"] = visited;

        // Next options - read travelable state from UI nodes (read-only)
        var nextOptions = new List<Dictionary<string, object?>>();
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null)
        {
            var travelable = FindAll<NMapPoint>(mapScreen)
                .Where(mp => mp.State == MapPointState.Travelable && mp.Point != null)
                .OrderBy(mp => mp.Point!.coord.col)
                .ToList();

            int index = 0;
            foreach (var nmp in travelable)
            {
                var pt = nmp.Point;
                var option = new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["col"] = pt.coord.col,
                    ["row"] = pt.coord.row,
                    ["type"] = pt.PointType.ToString()
                };

                // 1-level lookahead
                var children = pt.Children
                    .OrderBy(c => c.coord.col)
                    .Select(c => new Dictionary<string, object?>
                    {
                        ["col"] = c.coord.col, ["row"] = c.coord.row,
                        ["type"] = c.PointType.ToString()
                    }).ToList();
                if (children.Count > 0)
                    option["leads_to"] = children;

                nextOptions.Add(option);
                index++;
            }
        }
        state["next_options"] = nextOptions;

        // Full map - all nodes organized for planning
        var nodes = new List<Dictionary<string, object?>>();

        // Starting point
        var start = map.StartingMapPoint;
        nodes.Add(BuildMapNode(start));

        // Grid nodes
        foreach (var pt in map.GetAllMapPoints())
            nodes.Add(BuildMapNode(pt));

        // Boss identity comes from the live act's EncounterModel — BossEncounter
        // throws if the act hasn't finished setup yet, so guard the access.
        EncounterModel? bossEncounter = null;
        try { bossEncounter = runState.Act.BossEncounter; } catch { }
        var secondBossEncounter = runState.Act.SecondBossEncounter;

        var primaryBossId = bossEncounter?.Id?.Entry;
        var primaryBossName = SafeGetText(() => bossEncounter?.Title);
        var bossNode = BuildMapNode(map.BossMapPoint);
        AddBossIdentity(bossNode, primaryBossId, primaryBossName);
        nodes.Add(bossNode);

        Dictionary<string, object?>? secondBoss = null;
        if (map.SecondBossMapPoint != null)
        {
            var secondBossId = secondBossEncounter?.Id?.Entry;
            var secondBossName = SafeGetText(() => secondBossEncounter?.Title);
            var secondBossNode = BuildMapNode(map.SecondBossMapPoint);
            AddBossIdentity(secondBossNode, secondBossId, secondBossName);
            nodes.Add(secondBossNode);
            secondBoss = BuildBossInfo(map.SecondBossMapPoint, secondBossId, secondBossName);
        }

        state["nodes"] = nodes;
        var primaryBoss = BuildBossInfo(map.BossMapPoint, primaryBossId, primaryBossName);
        state["boss"] = primaryBoss;
        state["bosses"] = secondBoss != null
            ? new List<Dictionary<string, object?>> { primaryBoss, secondBoss }
            : new List<Dictionary<string, object?>> { primaryBoss };

        return state;
    }

    private static Dictionary<string, object?> BuildBossInfo(MapPoint pt, string? bossId, string? bossName)
    {
        var boss = new Dictionary<string, object?>
        {
            ["col"] = pt.coord.col,
            ["row"] = pt.coord.row
        };
        AddBossIdentity(boss, bossId, bossName);
        return boss;
    }

    private static void AddBossIdentity(Dictionary<string, object?> target, string? bossId, string? bossName)
    {
        if (string.IsNullOrWhiteSpace(bossId))
            return;

        target["id"] = bossId;
        if (!string.IsNullOrWhiteSpace(bossName))
            target["name"] = bossName;
    }

    private static Dictionary<string, object?> BuildMapNode(MapPoint pt)
    {
        return new Dictionary<string, object?>
        {
            ["col"] = pt.coord.col,
            ["row"] = pt.coord.row,
            ["type"] = pt.PointType.ToString(),
            ["children"] = pt.Children
                .OrderBy(c => c.coord.col)
                .Select(c => new List<int> { c.coord.col, c.coord.row })
                .ToList()
        };
    }
}
