using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.V2;

public static class RunScreenProjector
{
    public static string Snake(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string StripCategory(string id)
    {
        int dot = id.IndexOf('.');
        return dot > 0 && dot < id.Length - 1 && id[..dot].All(ch => char.IsUpper(ch) || ch == '_') ? id[(dot + 1)..] : id;
    }

    public static string ScreenOf(RunSim sim)
    {
        if (sim.HasPendingChoice)
        {
            return Screens.Select;
        }
        return sim.Phase switch
        {
            RunPhase.Map => Screens.Map,
            RunPhase.Combat => Screens.Combat,
            RunPhase.Rewards => Screens.Rewards,
            RunPhase.CardReward => Screens.CardReward,
            RunPhase.Shop => Screens.Shop,
            RunPhase.RestSite => Screens.RestSite,
            RunPhase.Treasure => Screens.Treasure,
            RunPhase.Event => Screens.Event,
            RunPhase.Prompt => Screens.Select,
            RunPhase.Victory => Screens.Victory,
            RunPhase.Defeat => Screens.Defeat,
            _ => Snake(sim.Phase.ToString())
        };
    }

    public static JsonObject Project(RunSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var rs = sim.State;
        string screen = ScreenOf(sim);
        var obj = new JsonObject { ["screen"] = screen };
        var room = RoomStack.CurrentRoom(rs);
        obj["room"] = Room(room);
        switch (screen)
        {
            case Screens.Map:
                obj["map"] = Map(rs);
                break;
            case Screens.Combat:
                if (rs.Combat != null)
                {
                    obj["combat"] = ProjectionJson.Write(SimProjector.Project(rs.Combat));
                }
                break;
            case Screens.Rewards:
                obj["rewards"] = Rewards(RunLegalActionGenerator.TopRewards(rs));
                break;
            case Screens.CardReward:
                obj["card_reward"] = CardRewardScreen(RunLegalActionGenerator.OpenCardReward(rs, sim.Loop));
                break;
            case Screens.Shop:
                obj["shop"] = Shop(rs, room as MerchantRoom);
                break;
            case Screens.RestSite:
                obj["rest_site"] = RestSite(room as RestSiteRoom);
                break;
            case Screens.Treasure:
                obj["treasure"] = Treasure(room as TreasureRoom);
                break;
            case Screens.Event:
                obj["event"] = Event(RunLegalActionGenerator.CurrentEvent(rs));
                break;
            case Screens.Select:
                obj["select"] = Select(sim.PendingChoice, sim.PendingPicks);
                if (rs.Combat is { IsInProgress: true } cs)
                {
                    obj["combat"] = ProjectionJson.Write(SimProjector.Project(cs));
                }
                break;
        }
        if (rs.Players.Count > 0)
        {
            obj["player"] = Player(rs.Players[0]);
            obj["deck"] = Deck(rs.Players[0]);
        }
        return obj;
    }

    public static JsonNode? Room(AbstractRoom? room)
    {
        if (room == null)
        {
            return null;
        }
        var obj = new JsonObject { ["room_type"] = Snake(room.RoomType.ToString()) };
        obj["model_id"] = room.ModelId;
        return obj;
    }

    public static JsonObject Map(RunState rs)
    {
        var obj = new JsonObject();
        var current = RunLoop.CurrentMapCoord(rs);
        obj["current"] = current is { } c ? Coord(c) : null;
        var legal = new List<MapPoint>();
        if (rs.Map != null)
        {
            RunLegalActionGenerator.GetLegalMapPoints(rs, legal);
        }
        var next = new JsonArray();
        foreach (var p in legal)
        {
            var o = Coord(p.coord);
            o["type"] = Snake(p.PointType.ToString());
            next.Add(o);
        }
        obj["next_options"] = next;
        if (rs.Acts.Count > rs.CurrentActIndex && rs.CurrentActIndex >= 0)
        {
            var rooms = rs.Act.Rooms;
            var bosses = new JsonArray();
            if (rooms.Boss != null) bosses.Add(rooms.Boss.Id);
            if (rooms.SecondBoss != null) bosses.Add(rooms.SecondBoss.Id);
            obj["boss_ids"] = bosses;
        }
        if (rs.Map != null)
        {
            var nodes = new JsonArray();
            foreach (var p in rs.Map.GetAllMapPoints().OrderBy(p => p.coord.row).ThenBy(p => p.coord.col))
            {
                var o = Coord(p.coord);
                o["type"] = Snake(p.PointType.ToString());
                var children = new JsonArray();
                foreach (var ch in p.Children.OrderBy(x => x.coord.col).ThenBy(x => x.coord.row))
                {
                    children.Add(Coord(ch.coord));
                }
                o["children"] = children;
                nodes.Add(o);
            }
            obj["nodes"] = nodes;
        }
        return obj;
    }

    public static JsonObject Coord(MapCoord c) => new() { ["col"] = c.col, ["row"] = c.row };

    public static JsonObject Rewards(RewardsSet? set)
    {
        var items = new JsonArray();
        if (set != null)
        {
            foreach (var reward in set.Rewards)
            {
                items.Add(RewardItem(reward));
            }
        }
        return new JsonObject { ["items"] = items };
    }

    public static string RewardTypeName(RewardType type) => type switch
    {
        RewardType.Card => "card",
        RewardType.Gold => "gold",
        RewardType.Potion => "potion",
        RewardType.Relic => "relic",
        RewardType.RemoveCard => "card_removal",
        RewardType.SpecialCard => "special_card",
        _ => Snake(type.ToString())
    };

    public static JsonObject RewardItem(Reward reward)
    {
        var o = new JsonObject { ["type"] = RewardTypeName(reward.RewardType), ["claimed"] = reward.SuccessfullySelected };
        switch (reward)
        {
            case GoldReward g:
                o["gold"] = g.Amount;
                break;
            case PotionReward p:
                o["potion_id"] = p.PotionId;
                break;
            case RelicReward r:
                o["relic_id"] = r.RelicId;
                break;
            case CardReward c when c.IsPopulated:
                o["cards"] = Cards(c.Cards.Select(x => x.Card));
                break;
            case SpecialCardReward s:
                o["cards"] = Cards(new[] { s.Card });
                break;
        }
        return o;
    }

    public static JsonArray Cards(IEnumerable<CardInstance> cards)
    {
        var arr = new JsonArray();
        foreach (var card in cards)
        {
            arr.Add(CardJson(card));
        }
        return arr;
    }

    public static JsonObject CardJson(CardInstance card)
    {
        var o = new JsonObject { ["id"] = card.Id, ["upgraded"] = card.IsUpgraded };
        if (card.Enchantment is { } e)
        {
            o["enchantment"] = new JsonObject { ["id"] = e.Id, ["amount"] = e.Amount };
        }
        return o;
    }

    public static JsonObject CardRewardScreen(CardReward? reward)
    {
        var o = new JsonObject();
        o["cards"] = reward == null ? new JsonArray() : Cards(reward.Cards.Select(x => x.Card));
        var alts = new JsonArray();
        if (reward != null)
        {
            foreach (var alt in reward.Alternatives)
            {
                alts.Add(alt.OptionId);
            }
        }
        o["alternatives"] = alts;
        return o;
    }

    public static string EntryCategory(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry c when c.CreationResult != null && MerchantCardEntry.IsColorless(c.CreationResult.Card) => "colorless_card",
        MerchantCardEntry => "card",
        MerchantRelicEntry => "relic",
        MerchantPotionEntry => "potion",
        MerchantCardRemovalEntry => "card_removal",
        _ => Snake(entry.GetType().Name)
    };

    public static string? EntryItemId(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry c => c.CreationResult?.Card.Id,
        MerchantRelicEntry r => r.Model?.Id,
        MerchantPotionEntry p => p.Model?.Id,
        _ => null
    };

    public static JsonObject Shop(RunState rs, MerchantRoom? room)
    {
        var entries = new JsonArray();
        if (room?.Inventory is { } inventory)
        {
            foreach (var entry in inventory.AllEntries)
            {
                var o = new JsonObject { ["category"] = EntryCategory(entry), ["stocked"] = entry.IsStocked };
                if (EntryItemId(entry) is { } id)
                {
                    o["id"] = id;
                }
                if (entry is MerchantCardEntry ce && ce.CreationResult != null)
                {
                    o["upgraded"] = ce.CreationResult.Card.IsUpgraded;
                    o["on_sale"] = ce.IsOnSale;
                }
                if (entry.IsStocked)
                {
                    o["price"] = entry.GetCost(rs);
                }
                entries.Add(o);
            }
        }
        return new JsonObject { ["entries"] = entries };
    }

    public static JsonObject RestSite(RestSiteRoom? room)
    {
        var options = new JsonArray();
        if (room != null)
        {
            foreach (var option in room.Options)
            {
                options.Add(new JsonObject { ["id"] = option.OptionId, ["enabled"] = option.IsEnabled });
            }
        }
        return new JsonObject { ["options"] = options };
    }

    public static JsonObject Treasure(TreasureRoom? room)
    {
        var relics = new JsonArray();
        if (room?.CurrentRelicIds is { } ids)
        {
            foreach (var id in ids)
            {
                relics.Add(id);
            }
        }
        return new JsonObject { ["opened"] = room?.ChestOpened ?? false, ["relics"] = relics };
    }

    public static JsonObject Event(EventModel? ev)
    {
        var o = new JsonObject { ["id"] = ev?.Id, ["finished"] = ev?.IsFinished ?? false };
        var options = new JsonArray();
        if (ev != null && !ev.IsFinished)
        {
            foreach (var option in ev.CurrentOptions)
            {
                var oo = new JsonObject { ["key"] = option.TextKey, ["locked"] = option.IsLocked, ["proceed"] = option.IsProceed };
                if (option.RelicId != null)
                {
                    oo["relic_id"] = option.RelicId;
                }
                options.Add(oo);
            }
        }
        o["options"] = options;
        return o;
    }

    public static JsonObject Select(ChoicePrompt? prompt, IReadOnlyList<int> picks)
    {
        var o = new JsonObject();
        if (prompt == null)
        {
            return o;
        }
        o["kind"] = Snake(prompt.Kind.ToString());
        o["site_id"] = prompt.SiteId;
        var candidates = new JsonArray();
        foreach (var option in prompt.Options)
        {
            candidates.Add(new JsonObject { ["id"] = option.ModelId, ["upgraded"] = option.UpgradeLevel > 0 });
        }
        o["candidates"] = candidates;
        o["min"] = prompt.MinSelect;
        o["max"] = prompt.MaxSelect;
        var picked = new JsonArray();
        foreach (var p in picks)
        {
            picked.Add(p);
        }
        o["picked"] = picked;
        return o;
    }

    public static JsonObject Player(PlayerState p)
    {
        var relics = new JsonArray();
        foreach (var relic in p.Relics)
        {
            relics.Add(relic.Id);
        }
        var potions = new JsonArray();
        for (int i = 0; i < p.PotionSlots.Length; i++)
        {
            if (p.PotionSlots[i] is { } potion)
            {
                potions.Add(new JsonObject { ["slot"] = i, ["id"] = potion.Id });
            }
        }
        return new JsonObject
        {
            ["hp"] = p.CurrentHp,
            ["max_hp"] = p.MaxHp,
            ["gold"] = p.Gold,
            ["relics"] = relics,
            ["potions"] = potions,
            ["max_potion_slots"] = p.PotionSlots.Length
        };
    }

    public static JsonArray Deck(PlayerState p) => Cards(p.Deck);

    public static JsonObject FloorSummary(MapPointHistoryEntry entry)
    {
        var rooms = new JsonArray();
        foreach (var room in entry.Rooms)
        {
            var monsters = new JsonArray();
            foreach (var m in room.MonsterIds)
            {
                monsters.Add(m);
            }
            rooms.Add(new JsonObject
            {
                ["room_type"] = Snake(room.RoomType.ToString()),
                ["model_id"] = room.ModelId,
                ["monster_ids"] = monsters,
                ["turns_taken"] = room.TurnsTaken
            });
        }
        return new JsonObject
        {
            ["map_point_type"] = Snake(entry.MapPointType.ToString()),
            ["rooms"] = rooms,
            ["gold_gained"] = entry.GoldGained
        };
    }
}
