using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

// Builds the OBS object of sim/PLAY.md: structured fields for the current phase plus a compact text rendering.
public static class ObservationBuilder
{
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static JsonObject Build(GameSession g, IReadOnlyList<LegalEntry> legal)
    {
        var rs = g.Run?.State;
        var cs = g.LiveCombat;
        var player = g.Player;
        var obs = new JsonObject
        {
            ["game_id"] = g.Id,
            ["kind"] = g.Kind == GameKind.Run ? "run" : "combat",
            ["phase"] = g.PhaseName,
            ["terminal"] = g.IsTerminal,
            ["result"] = g.Result,
            ["act"] = rs != null ? rs.CurrentActIndex + 1 : null,
            ["floor"] = rs != null ? rs.TotalFloor : null,
            ["turn"] = cs != null ? cs.Player.TurnNumber : null,
            ["player"] = PlayerNode(player, cs),
            ["combat"] = cs != null ? CombatNode(cs) : null,
            ["map"] = null,
            ["rewards"] = null,
            ["card_reward"] = null,
            ["shop"] = null,
            ["rest"] = null,
            ["treasure"] = null,
            ["event"] = null,
            ["prompt"] = PromptNode(g)
        };
        if (g.Run != null && !g.IsTerminal && g.PendingChoice == null)
        {
            var loop = g.Run.Loop;
            switch (g.Run.Phase)
            {
                case RunPhase.Map:
                    obs["map"] = MapNode(rs!);
                    break;
                case RunPhase.Rewards:
                    obs["rewards"] = RewardsNode(rs!);
                    break;
                case RunPhase.CardReward:
                    obs["card_reward"] = CardRewardNode(rs!, loop);
                    break;
                case RunPhase.Shop:
                    obs["shop"] = ShopNode(rs!);
                    break;
                case RunPhase.RestSite:
                    obs["rest"] = RestNode(rs!);
                    break;
                case RunPhase.Treasure:
                    obs["treasure"] = TreasureNode(rs!);
                    break;
                case RunPhase.Event:
                    obs["event"] = EventNode(rs!);
                    break;
            }
        }
        var legalNode = new JsonArray();
        foreach (var e in legal)
        {
            legalNode.Add(new JsonObject { ["token"] = e.Token, ["desc"] = e.Desc });
        }
        obs["legal_actions"] = legalNode;
        obs["text"] = Text(g, obs, legal);
        return obs;
    }

    private static JsonObject PlayerNode(PlayerState p, CombatState? cs)
    {
        int maxEnergy = p.BaseMaxEnergy;
        if (cs != null)
        {
            try
            {
                maxEnergy = EnergyCmd.GetMaxEnergy(cs, p);
            }
            catch (Exception)
            {
            }
        }
        var node = new JsonObject
        {
            ["character"] = p.CharacterId,
            ["hp"] = p.CurrentHp,
            ["max_hp"] = p.MaxHp,
            ["block"] = p.Block,
            ["energy"] = cs != null ? p.Energy : maxEnergy,
            ["max_energy"] = maxEnergy,
            ["gold"] = p.Gold
        };
        if (p.CharacterId == "REGENT" || p.Stars != 0)
        {
            node["stars"] = p.Stars;
        }
        var powers = new JsonArray();
        if (cs != null)
        {
            foreach (var (id, amount) in Describe.Powers(cs, p))
            {
                powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount });
            }
        }
        node["powers"] = powers;
        var relics = new JsonArray();
        foreach (var relic in p.Relics)
        {
            var r = new JsonObject { ["id"] = relic.Id };
            try
            {
                if (relic.ShowCounter)
                {
                    r["counter"] = relic.DisplayAmount;
                }
            }
            catch (Exception)
            {
            }
            relics.Add(r);
        }
        node["relics"] = relics;
        var potions = new JsonArray();
        for (int i = 0; i < p.PotionSlots.Length; i++)
        {
            var potion = p.PotionSlots[i];
            potions.Add(potion == null ? null : new JsonObject { ["slot"] = i, ["id"] = potion.Id });
        }
        node["potions"] = potions;
        if (p.OrbQueue.Capacity > 0 || p.OrbQueue.Orbs.Count > 0)
        {
            var orbs = new JsonArray();
            foreach (var orb in p.OrbQueue.Orbs)
            {
                var o = new JsonObject { ["id"] = orb.Id };
                if (cs != null)
                {
                    try
                    {
                        o["passive"] = (int)orb.PassiveVal(cs);
                        o["evoke"] = (int)orb.EvokeVal(cs);
                    }
                    catch (Exception)
                    {
                    }
                }
                orbs.Add(o);
            }
            node["orbs"] = orbs;
            node["orb_slots"] = p.OrbQueue.Capacity;
        }
        node["deck_size"] = p.Deck.Count;
        var deck = new JsonArray();
        foreach (var card in p.Deck)
        {
            deck.Add(Describe.CardName(card));
        }
        node["deck"] = deck;
        return node;
    }

    private static JsonObject CardNode(CombatState? cs, CardInstance card, int index, bool withPlayability)
    {
        var node = new JsonObject
        {
            ["index"] = index,
            ["id"] = card.Id,
            ["upgraded"] = card.IsUpgraded,
            ["cost"] = Describe.CostNode(cs, card)
        };
        if (Describe.StarCost(cs, card) is { } stars)
        {
            node["star_cost"] = stars;
        }
        node["type"] = card.Type.ToString();
        node["target"] = card.TargetType.ToString();
        node["rarity"] = card.Rarity.ToString();
        var keywords = new JsonArray();
        foreach (var k in Describe.Keywords(cs, card))
        {
            keywords.Add(k);
        }
        node["keywords"] = keywords;
        var vars = new JsonObject();
        foreach (var (name, value) in Describe.Vars(card))
        {
            vars[name] = value;
        }
        node["vars"] = vars;
        if (withPlayability && cs != null)
        {
            bool ok = Describe.Playable(cs, card, out var reason);
            node["playable"] = ok;
            node["reason"] = reason;
        }
        return node;
    }

    private static JsonObject CombatNode(CombatState cs)
    {
        var player = cs.Player;
        var hand = new JsonArray();
        for (int i = 0; i < player.Hand.Count; i++)
        {
            hand.Add(CardNode(cs, player.Hand[i], i, true));
        }
        var enemies = new JsonArray();
        foreach (var (index, m) in Describe.LiveEnemies(cs))
        {
            var intents = new JsonArray();
            foreach (var intent in Describe.Intents(cs, m))
            {
                var it = new JsonObject { ["type"] = intent.Type };
                if (intent.AttackValue is { } dmg)
                {
                    it["damage"] = dmg;
                    it["times"] = intent.AttackTimes ?? 1;
                }
                if (intent.Extras.TryGetValue(SimProjector.StatusCardCountKey, out var count) && count != null)
                {
                    it["card_count"] = count.DeepClone();
                }
                intents.Add(it);
            }
            var powers = new JsonArray();
            foreach (var (id, amount) in Describe.Powers(cs, m))
            {
                powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount });
            }
            enemies.Add(new JsonObject
            {
                ["index"] = index,
                ["id"] = m.MonsterId,
                ["hp"] = m.CurrentHp,
                ["max_hp"] = m.MaxHp,
                ["block"] = m.Block,
                ["intents"] = intents,
                ["powers"] = powers
            });
        }
        var allies = new JsonArray();
        for (int a = 1; a < cs.Allies.Count; a++)
        {
            var ally = cs.Allies[a];
            if (ally is MonsterState pet && pet.IsAlive)
            {
                var powers = new JsonArray();
                foreach (var (id, amount) in Describe.Powers(cs, pet))
                {
                    powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount });
                }
                allies.Add(new JsonObject { ["index"] = a, ["id"] = pet.MonsterId, ["hp"] = pet.CurrentHp, ["max_hp"] = pet.MaxHp, ["block"] = pet.Block, ["powers"] = powers });
            }
        }
        var draw = SimProjector.PileKeys(player.DrawPile);
        draw.Sort(StringComparer.Ordinal);
        var node = new JsonObject
        {
            ["hand"] = hand,
            ["draw_count"] = player.DrawPile.Count,
            ["discard_count"] = player.DiscardPile.Count,
            ["exhaust_count"] = player.ExhaustPile.Count,
            ["draw_pile"] = new JsonArray(draw.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["discard_pile"] = new JsonArray(SimProjector.PileKeys(player.DiscardPile).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["enemies"] = enemies
        };
        if (allies.Count > 0)
        {
            node["allies"] = allies;
        }
        return node;
    }

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
            if (entry is MerchantCardEntry { IsOnSale: true })
            {
                node["on_sale"] = true;
            }
            entries.Add(node);
        }
        return new JsonObject { ["entries"] = entries, ["removal_price"] = removalPrice };
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
                var node = new JsonObject { ["index"] = i, ["key"] = opt.TextKey, ["enabled"] = opt.IsEnabled };
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
            ["page"] = ev.Description,
            ["finished"] = ev.IsFinished,
            ["options"] = options
        };
    }

    private static JsonObject? PromptNode(GameSession g)
    {
        var p = g.PendingChoice;
        if (p == null)
        {
            return null;
        }
        var picks = g.PendingPicks;
        var picked = new JsonArray();
        foreach (var i in picks)
        {
            picked.Add(i);
        }
        var options = new JsonArray();
        for (int i = 0; i < p.Options.Count; i++)
        {
            var o = p.Options[i];
            options.Add(new JsonObject { ["index"] = i, ["id"] = o.ModelId, ["upgraded"] = o.UpgradeLevel > 0 });
        }
        bool canConfirm;
        try
        {
            canConfirm = ChoiceRuntime.CanConfirm(p, picks.Count);
        }
        catch (Exception)
        {
            canConfirm = false;
        }
        return new JsonObject
        {
            ["kind"] = p.Kind.ToString(),
            ["site"] = p.SiteId,
            ["min"] = p.MinSelect,
            ["max"] = p.MaxSelect,
            ["picked"] = picked,
            ["options"] = options,
            ["can_confirm"] = canConfirm,
            ["can_cancel"] = p.Cancelable
        };
    }

    private static string Text(GameSession g, JsonObject obs, IReadOnlyList<LegalEntry> legal)
    {
        var sb = new StringBuilder(2048);
        var rs = g.Run?.State;
        var cs = g.LiveCombat;
        var p = g.Player;
        sb.Append("== ").Append(g.Id).Append(' ').Append(g.PhaseName);
        if (rs != null)
        {
            sb.Append(" | act ").Append(I(rs.CurrentActIndex + 1)).Append(" floor ").Append(I(rs.TotalFloor));
        }
        if (cs != null)
        {
            sb.Append(" | turn ").Append(I(cs.Player.TurnNumber));
        }
        sb.Append(" | ").Append(p.CharacterId).Append(" HP ").Append(I(p.CurrentHp)).Append('/').Append(I(p.MaxHp));
        if (cs != null)
        {
            var player = (JsonObject)obs["player"]!;
            sb.Append(" block ").Append(I(p.Block)).Append(" energy ").Append(I(p.Energy)).Append('/').Append(player["max_energy"]!.ToJsonString());
        }
        if (p.CharacterId == "REGENT" || p.Stars != 0)
        {
            sb.Append(" stars ").Append(I(p.Stars));
        }
        sb.Append(" gold ").Append(I(p.Gold));
        if (g.IsTerminal)
        {
            sb.Append(" | RESULT ").Append(g.Result);
        }
        sb.Append('\n');
        if (cs != null)
        {
            var powers = Describe.Powers(cs, p);
            if (powers.Count > 0)
            {
                sb.Append("powers: ").Append(Describe.PowersText(powers)).Append('\n');
            }
        }
        if (p.Relics.Count > 0)
        {
            sb.Append("relics: ").Append(string.Join(", ", p.Relics.Select(r => RelicText(r)))).Append('\n');
        }
        sb.Append("potions:");
        for (int i = 0; i < p.PotionSlots.Length; i++)
        {
            sb.Append(" [").Append(I(i)).Append("] ").Append(p.PotionSlots[i]?.Id ?? "-");
        }
        sb.Append('\n');
        if (p.OrbQueue.Capacity > 0)
        {
            sb.Append("orbs (").Append(I(p.OrbQueue.Capacity)).Append(" slots): ").Append(p.OrbQueue.Orbs.Count == 0 ? "-" : string.Join(", ", p.OrbQueue.Orbs.Select(o => o.Id))).Append('\n');
        }
        sb.Append("deck(").Append(I(p.Deck.Count)).Append("): ").Append(Describe.DeckSummary(p.Deck)).Append('\n');
        if (cs != null)
        {
            sb.Append("-- enemies\n");
            foreach (var (index, m) in Describe.LiveEnemies(cs))
            {
                sb.Append(" [").Append(I(index)).Append("] ").Append(m.MonsterId).Append(' ').Append(I(m.CurrentHp)).Append('/').Append(I(m.MaxHp));
                if (m.Block > 0)
                {
                    sb.Append(" block ").Append(I(m.Block));
                }
                var intents = Describe.Intents(cs, m);
                sb.Append(" | intent: ").Append(intents.Count == 0 ? "-" : string.Join(" + ", intents.Select(Describe.IntentText)));
                var powers = Describe.Powers(cs, m);
                if (powers.Count > 0)
                {
                    sb.Append(" | ").Append(Describe.PowersText(powers));
                }
                sb.Append('\n');
            }
            for (int a = 1; a < cs.Allies.Count; a++)
            {
                if (cs.Allies[a] is MonsterState pet && pet.IsAlive)
                {
                    sb.Append(" ally [").Append(I(a)).Append("] ").Append(pet.MonsterId).Append(' ').Append(I(pet.CurrentHp)).Append('/').Append(I(pet.MaxHp)).Append('\n');
                }
            }
            var player = cs.Player;
            sb.Append("-- hand (draw ").Append(I(player.DrawPile.Count)).Append(", discard ").Append(I(player.DiscardPile.Count)).Append(", exhaust ").Append(I(player.ExhaustPile.Count)).Append(")\n");
            for (int i = 0; i < player.Hand.Count; i++)
            {
                var card = player.Hand[i];
                sb.Append(" [").Append(I(i)).Append("] ").Append(Describe.CardName(card)).Append(' ').Append(Describe.CostText(cs, card));
                if (Describe.StarCost(cs, card) is { } stars)
                {
                    sb.Append(' ').Append(I(stars)).Append('*');
                }
                sb.Append(' ').Append(card.Type).Append("->").Append(card.TargetType);
                string vars = Describe.VarSummary(card);
                if (vars.Length > 0)
                {
                    sb.Append(" (").Append(vars).Append(')');
                }
                var keywords = Describe.Keywords(cs, card);
                if (keywords.Count > 0)
                {
                    sb.Append(" {").Append(string.Join(",", keywords)).Append('}');
                }
                if (!Describe.Playable(cs, card, out var reason))
                {
                    sb.Append(" UNPLAYABLE:").Append(reason);
                }
                sb.Append('\n');
            }
        }
        AppendScreen(sb, obs);
        sb.Append("-- legal actions\n");
        foreach (var e in legal)
        {
            sb.Append(' ').Append(e.Token).Append("  : ").Append(e.Desc).Append('\n');
        }
        return sb.ToString();
    }

    private static string RelicText(RelicModel r)
    {
        try
        {
            return r.ShowCounter ? $"{r.Id}({I(r.DisplayAmount)})" : r.Id;
        }
        catch (Exception)
        {
            return r.Id;
        }
    }

    private static void AppendScreen(StringBuilder sb, JsonObject obs)
    {
        if (obs["prompt"] is JsonObject prompt)
        {
            sb.Append("-- prompt ").Append(prompt["kind"]).Append(" at ").Append(prompt["site"]).Append(": pick ").Append(prompt["min"]!.ToJsonString()).Append("..").Append(prompt["max"]!.ToJsonString())
                .Append(", picked [").Append(string.Join(",", ((JsonArray)prompt["picked"]!).Select(n => n!.ToJsonString()))).Append("]\n");
            foreach (var o in (JsonArray)prompt["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["id"]).Append(o["upgraded"]!.GetValue<bool>() ? "+" : string.Empty).Append('\n');
            }
        }
        if (obs["map"] is JsonObject map)
        {
            sb.Append("-- map: next rooms\n");
            foreach (var o in (JsonArray)map["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["type"]).Append(" col ").Append(o["col"]!.ToJsonString()).Append(" row ").Append(o["row"]!.ToJsonString())
                    .Append(" -> ").Append(string.Join("/", ((JsonArray)o["leads_to"]!).Select(n => n!.GetValue<string>()))).Append('\n');
            }
        }
        if (obs["rewards"] is JsonArray rewards)
        {
            sb.Append("-- rewards\n");
            foreach (var r in rewards)
            {
                sb.Append(" (").Append(r!["index"]!.ToJsonString()).Append(") ").Append(r["type"]).Append(": ").Append(r["detail"]).Append('\n');
            }
        }
        if (obs["card_reward"] is JsonObject cr)
        {
            sb.Append("-- card reward (pick one").Append(cr["can_skip"]!.GetValue<bool>() ? " or skip" : string.Empty).Append(")\n");
            foreach (var c in (JsonArray)cr["cards"]!)
            {
                sb.Append(" (").Append(c!["index"]!.ToJsonString()).Append(") ").Append(c["id"]).Append(c["upgraded"]!.GetValue<bool>() ? "+" : string.Empty)
                    .Append(' ').Append(c["type"]).Append(' ').Append(c["rarity"]).Append(" cost ").Append(c["cost"]!.ToJsonString().Trim('"'));
                var vars = (JsonObject)c["vars"]!;
                if (vars.Count > 0)
                {
                    sb.Append(" (").Append(string.Join(", ", vars.Select(kv => kv.Key + "=" + kv.Value!.ToJsonString()))).Append(')');
                }
                var kws = (JsonArray)c["keywords"]!;
                if (kws.Count > 0)
                {
                    sb.Append(" {").Append(string.Join(",", kws.Select(k => k!.GetValue<string>()))).Append('}');
                }
                sb.Append('\n');
            }
        }
        if (obs["shop"] is JsonObject shop)
        {
            sb.Append("-- shop (card removal: ").Append(shop["removal_price"] is { } rp ? rp.ToJsonString() + "g" : "n/a").Append(")\n");
            foreach (var e in (JsonArray)shop["entries"]!)
            {
                sb.Append(" (").Append(e!["index"]!.ToJsonString()).Append(") ").Append(e["category"]).Append(' ').Append(e["id"]?.GetValue<string>() ?? "-")
                    .Append(' ').Append(e["price"]!.ToJsonString()).Append('g').Append(e["sold"]!.GetValue<bool>() ? " SOLD" : string.Empty).Append('\n');
            }
        }
        if (obs["rest"] is JsonObject rest)
        {
            sb.Append("-- rest site\n");
            foreach (var o in (JsonArray)rest["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["id"]).Append(o["enabled"]!.GetValue<bool>() ? string.Empty : " (disabled)").Append('\n');
            }
        }
        if (obs["treasure"] is JsonObject t)
        {
            sb.Append("-- treasure: chest ").Append(t["chest_opened"]!.GetValue<bool>() ? "open" : "closed");
            if (t["relic"] is { } relic)
            {
                sb.Append(", relic ").Append(relic.GetValue<string>());
            }
            sb.Append('\n');
        }
        if (obs["event"] is JsonObject ev)
        {
            sb.Append("-- event ").Append(ev["id"]).Append(" page ").Append(ev["page"]?.GetValue<string>() ?? "-").Append(ev["finished"]!.GetValue<bool>() ? " (finished)" : string.Empty).Append('\n');
            foreach (var o in (JsonArray)ev["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["key"]).Append(o["enabled"]!.GetValue<bool>() ? string.Empty : " (disabled)");
                if (o["relic"] is { } r)
                {
                    sb.Append(" relic ").Append(r.GetValue<string>());
                }
                sb.Append('\n');
            }
        }
    }
}
