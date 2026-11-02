using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
    private static JsonObject MapNode(RunState rs)
    {
        var points = new List<MapPoint>();
        RunLegalActionGenerator.GetLegalMapPoints(rs, points);
        var options = new JsonArray();
        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            var leads = new JsonArray();
            foreach (var child in pt.Children.OrderBy(c => c.coord.col))
            {
                leads.Add(child.PointType.ToString());
            }
            options.Add(new JsonObject
            {
                ["index"] = i,
                ["type"] = pt.PointType.ToString(),
                ["col"] = pt.coord.col,
                ["row"] = pt.coord.row,
                ["leads_to"] = leads
            });
        }
        return new JsonObject { ["options"] = options, ["nodes"] = MapNodes(rs) };
    }

    // The whole act map: every room with its row, column, type, the columns it leads to in the next row,
    // and whether it was visited / is the current room. Sorted by row, then column.
    private static JsonArray MapNodes(RunState rs)
    {
        var list = new JsonArray();
        var map = rs.Map;
        if (map == null)
        {
            return list;
        }
        var points = new List<MapPoint>();
        var seen = new HashSet<(int, int)>();
        void Add(MapPoint? pt)
        {
            if (pt != null && seen.Add((pt.coord.row, pt.coord.col)))
            {
                points.Add(pt);
            }
        }
        Add(map.StartingMapPoint);
        foreach (var pt in map.GetAllMapPoints())
        {
            Add(pt);
        }
        Add(map.BossMapPoint);
        Add(map.SecondBossMapPoint);
        points.Sort((a, b) => a.coord.row != b.coord.row ? a.coord.row.CompareTo(b.coord.row) : a.coord.col.CompareTo(b.coord.col));
        var visited = new HashSet<MapCoord>(rs.VisitedMapCoords);
        var current = rs.CurrentMapCoord;
        foreach (var pt in points)
        {
            var edges = new JsonArray();
            foreach (var child in pt.Children.OrderBy(c => c.coord.col))
            {
                edges.Add(child.coord.col);
            }
            list.Add(new JsonObject
            {
                ["row"] = pt.coord.row,
                ["col"] = pt.coord.col,
                ["type"] = pt.PointType.ToString(),
                ["edges"] = edges,
                ["visited"] = visited.Contains(pt.coord),
                ["current"] = current is { } cur && cur == pt.coord
            });
        }
        return list;
    }

    private static char MapLetter(string? type) => type switch
    {
        "Monster" => 'M',
        "Elite" => 'E',
        "RestSite" => 'R',
        "Shop" => '$',
        "Treasure" => 'T',
        "Unknown" => '?',
        "Boss" => 'B',
        "Ancient" => 'A',
        _ => '_'
    };

    // Compact act map, one line per row (at most 19 rows, the rows nearest the current one when the map is
    // taller): "r3: 1M>1,2 4E*>3" = column 1 Monster leading to columns 1 and 2 of row 4; '*' current room,
    // 'x' visited.
    private static void AppendMapGrid(StringBuilder sb, JsonArray? nodes)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return;
        }
        var rows = new SortedDictionary<int, List<JsonNode>>();
        int currentRow = -1;
        foreach (var n in nodes)
        {
            if (n == null)
            {
                continue;
            }
            int row = n["row"]!.GetValue<int>();
            if (!rows.TryGetValue(row, out var list))
            {
                rows[row] = list = new List<JsonNode>();
            }
            list.Add(n);
            if (n["current"]!.GetValue<bool>())
            {
                currentRow = row;
            }
        }
        const int MaxRows = 19;
        var keys = rows.Keys.ToList();
        if (keys.Count > MaxRows)
        {
            int at = Math.Max(0, keys.IndexOf(currentRow));
            int first = Math.Clamp(at - 1, 0, keys.Count - MaxRows);
            keys = keys.GetRange(first, MaxRows);
        }
        sb.Append("-- act map (r<row>: <col><type>[*current|x visited]><next-row cols>; M monster E elite R rest $ shop T treasure ? unknown A ancient B boss)\n");
        foreach (var row in keys)
        {
            sb.Append(" r").Append(I(row)).Append(':');
            foreach (var n in rows[row])
            {
                sb.Append(' ').Append(n["col"]!.ToJsonString()).Append(MapLetter(n["type"]?.GetValue<string>()));
                if (n["current"]!.GetValue<bool>())
                {
                    sb.Append('*');
                }
                else if (n["visited"]!.GetValue<bool>())
                {
                    sb.Append('x');
                }
                var edges = (JsonArray)n["edges"]!;
                if (edges.Count > 0)
                {
                    sb.Append('>').Append(string.Join(",", edges.Select(c => c!.ToJsonString())));
                }
            }
            sb.Append('\n');
        }
    }

    private static JsonArray RewardsNode(RunState rs)
    {
        var list = new JsonArray();
        var set = RunLegalActionGenerator.TopRewards(rs);
        if (set == null)
        {
            return list;
        }
        for (int i = 0; i < set.Rewards.Count; i++)
        {
            var r = set.Rewards[i];
            if (r.SuccessfullySelected)
            {
                continue;
            }
            list.Add(new JsonObject { ["index"] = i, ["type"] = r.RewardType.ToString(), ["detail"] = ActionTokens.RewardDetail(r) });
        }
        return list;
    }

    private static JsonObject? CardRewardNode(RunState rs, RunLoopState loop)
    {
        var reward = RunLegalActionGenerator.OpenCardReward(rs, loop);
        if (reward == null)
        {
            return null;
        }
        var cards = new JsonArray();
        for (int i = 0; i < reward.Cards.Count; i++)
        {
            cards.Add(CardNode(null, reward.Cards[i].Card, i, false));
        }
        var alts = new JsonArray();
        bool canSkip = false;
        bool? skipConsumes = null;
        for (int i = 0; i < reward.Alternatives.Count; i++)
        {
            var alt = reward.Alternatives[i];
            if (alt.OptionId == CardRewardAlternative.SkipId)
            {
                canSkip = true;
                skipConsumes = alt.CompletesReward;
            }
            alts.Add(new JsonObject { ["index"] = i, ["id"] = alt.OptionId });
        }
        // skip_consumes: false = 'skip' closes the screen but the card reward stays on the rewards screen
        // (re-open it with 'reward <i>'; it is lost only on 'proceed'); true = skipping completes the reward.
        return new JsonObject { ["cards"] = cards, ["alternatives"] = alts, ["can_skip"] = canSkip, ["skip_consumes"] = skipConsumes };
    }

    private static JsonObject? ShopNode(RunState rs)
    {
        if (RoomStack.CurrentRoom(rs) is not MerchantRoom { Inventory: { } inventory })
        {
            return null;
        }
        var entries = new JsonArray();
        var all = inventory.AllEntries;
        JsonNode? removalPrice = null;
        for (int i = 0; i < all.Count; i++)
        {
            var entry = all[i];
            int price;
            bool stocked;
            try
            {
                price = entry.GetCost(rs);
                stocked = entry.IsStocked;
            }
            catch (Exception)
            {
                price = -1;
                stocked = false;
            }
            if (entry is MerchantCardRemovalEntry)
            {
                removalPrice = stocked ? price : null;
                continue;
            }
            var node = new JsonObject
            {
                ["index"] = i,
                ["category"] = ActionTokens.ShopCategory(inventory, i, entry),
                ["id"] = ActionTokens.ShopEntryId(entry),
                ["price"] = price,
                ["sold"] = !stocked
            };
            AddShopNameText(node, entry);
            bool onSale = entry is MerchantCardEntry { IsOnSale: true };
            node["on_sale"] = onSale;
            // A sale halves the (randomised) card price with integer division, so the pre-sale price is
            // 2x the sale price, up to 1 gold.
            node["base_price"] = onSale && price >= 0 ? price * 2 : null;
            entries.Add(node);
        }
        return new JsonObject { ["entries"] = entries, ["removal_price"] = removalPrice };
    }

    private static void AddShopNameText(JsonObject node, MerchantEntry entry)
    {
        string? name = null;
        string? text = null;
        switch (entry)
        {
            case MerchantCardEntry { CreationResult: { } created }:
                name = Rt(() => RulesText.CardTitle(created.Card.Id));
                text = Rt(() => RulesText.CardText(created.Card));
                break;
            case MerchantRelicEntry { Model: { } relic }:
                name = Rt(() => RulesText.RelicTitle(relic.Id));
                text = Rt(() => RulesText.RelicText(relic));
                break;
            case MerchantPotionEntry { Model: { } potion }:
                name = Rt(() => RulesText.PotionTitle(potion.Id));
                text = Rt(() => RulesText.PotionText(potion.Id));
                break;
        }
        node["name"] = name;
        node["text"] = text;
    }

    private static JsonObject? RestNode(RunState rs)
    {
        if (RoomStack.CurrentRoom(rs) is not RestSiteRoom room)
        {
            return null;
        }
        var options = new JsonArray();
        for (int i = 0; i < room.Options.Count; i++)
        {
            bool enabled;
            try
            {
                enabled = room.Options[i].IsEnabled;
            }
            catch (Exception)
            {
                enabled = false;
            }
            options.Add(new JsonObject { ["index"] = i, ["id"] = room.Options[i].OptionId, ["enabled"] = enabled });
        }
        return new JsonObject { ["options"] = options };
    }

    private static JsonObject? TreasureNode(RunState rs)
    {
        if (RoomStack.CurrentRoom(rs) is not TreasureRoom room)
        {
            return null;
        }
        return new JsonObject
        {
            ["chest_opened"] = room.ChestOpened,
            ["relic"] = room.ChestOpened ? room.CurrentRelicId : null,
            ["relic_name"] = room.ChestOpened && room.CurrentRelicId is { } rn ? Rt(() => RulesText.RelicTitle(rn)) : null,
            ["relic_text"] = room.ChestOpened && room.CurrentRelicId is { } rt ? Rt(() => RulesText.RelicText(rt)) : null,
            ["relic_claimed"] = room.RelicClaimed,
            ["skipped"] = room.SingleplayerSkipped,
            ["gold_gained"] = room.GoldGained
        };
    }

    private static JsonObject? EventNode(RunState rs)
    {
        var ev = RunLegalActionGenerator.CurrentEvent(rs);
        if (ev == null)
        {
            return null;
        }
        var options = new JsonArray();
        if (!ev.IsFinished)
        {
            for (int i = 0; i < ev.CurrentOptions.Count; i++)
            {
                var opt = ev.CurrentOptions[i];
                var node = new JsonObject { ["index"] = i, ["key"] = opt.TextKey, ["enabled"] = opt.IsEnabled, ["text"] = Rt(() => RulesText.EventOptionText(ev, opt)) };
                if (opt.RelicId != null)
                {
                    node["relic"] = opt.RelicId;
                }
                options.Add(node);
            }
        }
        return new JsonObject
        {
            ["id"] = ev.Id,
            ["name"] = Rt(() => RulesText.EventTitle(ev.Id)),
            ["page"] = ev.Description,
            ["finished"] = ev.IsFinished,
            ["options"] = options
        };
    }
}
