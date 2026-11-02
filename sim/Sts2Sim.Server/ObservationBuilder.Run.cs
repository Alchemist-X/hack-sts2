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
        return new JsonObject { ["options"] = options };
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
        for (int i = 0; i < reward.Alternatives.Count; i++)
        {
            var alt = reward.Alternatives[i];
            canSkip |= alt.OptionId == CardRewardAlternative.SkipId;
            alts.Add(new JsonObject { ["index"] = i, ["id"] = alt.OptionId });
        }
        return new JsonObject { ["cards"] = cards, ["alternatives"] = alts, ["can_skip"] = canSkip };
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
            if (entry is MerchantCardEntry { IsOnSale: true })
            {
                node["on_sale"] = true;
            }
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
                var node = new JsonObject { ["index"] = i, ["key"] = opt.TextKey, ["enabled"] = opt.IsEnabled, ["text"] = Rt(() => RulesText.EventOptionText(ev.Id, opt.TextKey)) };
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
