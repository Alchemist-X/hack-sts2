using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

// Builds the OBS object of sim/PLAY.md: structured fields for the current phase plus a compact text rendering.
public static partial class ObservationBuilder
{
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    // Exception-tolerant RulesText lookup: a failing lookup renders as "unavailable" (null), never as a crash.
    private static string? Rt(Func<string?> lookup)
    {
        try
        {
            return lookup();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? node, string key)
        => node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // " (Name)" after an id when the node carries a non-null name.
    private static StringBuilder AppendName(StringBuilder sb, JsonNode? node, string key = "name")
        => Str(node, key) is { } name ? sb.Append(" (").Append(Collapse(name)).Append(')') : sb;

    // " — text" at the end of an item line when the node carries a non-null rules text.
    private static StringBuilder AppendText(StringBuilder sb, JsonNode? node, string key = "text")
        => Str(node, key) is { } text ? sb.Append(" \u2014 ").Append(Collapse(text)) : sb;

    private static bool AnyNameOrText(JsonArray? items)
        => items != null && items.Any(n => Str(n, "name") != null || Str(n, "text") != null);

    // One indented line per power that has rules text: "<indent>ID — text".
    private static void AppendPowerTexts(StringBuilder sb, JsonNode? powers, string indent)
    {
        if (powers is not JsonArray list)
        {
            return;
        }
        foreach (var pw in list)
        {
            if (Str(pw, "text") is { } text)
            {
                sb.Append(indent).Append(pw!["id"]).Append(" \u2014 ").Append(Collapse(text)).Append('\n');
            }
        }
    }

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
            AppendDanger(sb, obs["combat"]?["danger"] as JsonObject, p.MaxHp);
            if (obs["player"]?["powers"] is JsonArray { Count: > 0 } playerPowers)
            {
                sb.Append("powers: ").Append(PowersSummary(playerPowers)).Append('\n');
                AppendPowerTexts(sb, playerPowers, "  ");
            }
        }
        var relicNodes = obs["player"]?["relics"] as JsonArray;
        if (p.Relics.Count > 0 && AnyNameOrText(relicNodes) && relicNodes!.Count == p.Relics.Count)
        {
            sb.Append("relics:\n");
            for (int i = 0; i < p.Relics.Count; i++)
            {
                sb.Append(' ').Append(RelicText(p.Relics[i]));
                AppendName(sb, relicNodes[i]);
                AppendText(sb, relicNodes[i]).Append('\n');
            }
        }
        else if (p.Relics.Count > 0)
        {
            sb.Append("relics: ").Append(string.Join(", ", p.Relics.Select(r => RelicText(r)))).Append('\n');
        }
        var potionNodes = obs["player"]?["potions"] as JsonArray;
        if (AnyNameOrText(potionNodes) && potionNodes!.Count == p.PotionSlots.Length)
        {
            sb.Append("potions:\n");
            for (int i = 0; i < p.PotionSlots.Length; i++)
            {
                sb.Append(" [").Append(I(i)).Append("] ").Append(p.PotionSlots[i]?.Id ?? "-");
                AppendName(sb, potionNodes[i]);
                AppendText(sb, potionNodes[i]).Append('\n');
            }
        }
        else
        {
            sb.Append("potions:");
            for (int i = 0; i < p.PotionSlots.Length; i++)
            {
                sb.Append(" [").Append(I(i)).Append("] ").Append(p.PotionSlots[i]?.Id ?? "-");
            }
            sb.Append('\n');
        }
        if (p.OrbQueue.Capacity > 0)
        {
            sb.Append("orbs (").Append(I(p.OrbQueue.Capacity)).Append(" slots): ").Append(p.OrbQueue.Orbs.Count == 0 ? "-" : string.Join(", ", p.OrbQueue.Orbs.Select(o => o.Id))).Append('\n');
        }
        sb.Append("deck(").Append(I(p.Deck.Count)).Append("): ").Append(Describe.DeckSummary(p.Deck)).Append('\n');
        if (cs != null)
        {
            sb.Append("-- enemies");
            if (obs["player"]?["facing"] is JsonValue facing)
            {
                sb.Append(" (you face ").Append(facing.GetValue<string>()).Append(')');
            }
            sb.Append('\n');
            var combatNode = obs["combat"] as JsonObject;
            var enemyNodes = combatNode?["enemies"] as JsonArray;
            int e = 0;
            foreach (var (index, m) in Describe.LiveEnemies(cs))
            {
                var enemyNode = enemyNodes != null && e < enemyNodes.Count ? enemyNodes[e] : null;
                e++;
                sb.Append(" [").Append(I(index)).Append("] ").Append(Describe.Uid(m)).Append(' ').Append(m.MonsterId);
                AppendName(sb, enemyNode);
                sb.Append(' ').Append(I(m.CurrentHp)).Append('/').Append(I(m.MaxHp));
                if (m.Block > 0)
                {
                    sb.Append(" block ").Append(I(m.Block));
                }
                if (Str(enemyNode, "flank") is { } flank)
                {
                    sb.Append(" | ").Append(flank).Append(" side");
                    if (enemyNode!["behind"] is JsonValue behind && behind.GetValue<bool>())
                    {
                        sb.Append(" (BEHIND you: x1.5)");
                    }
                }
                var intents = Describe.Intents(cs, m);
                sb.Append(" | intent: ");
                if (enemyNode?["intents"] is JsonArray { Count: > 0 } intentNodes && Str(intentNodes[0], "move") is { } move)
                {
                    sb.Append('[').Append(move);
                    if (Str(intentNodes[0], "move_name") is { } moveName)
                    {
                        sb.Append(" (").Append(moveName).Append(')');
                    }
                    sb.Append("] ");
                }
                sb.Append(intents.Count == 0 ? "-" : string.Join(" + ", intents.Select(Describe.IntentText)));
                if (enemyNode?["powers"] is JsonArray { Count: > 0 } enemyPowers)
                {
                    sb.Append(" | ").Append(PowersSummary(enemyPowers));
                }
                sb.Append('\n');
                AppendPowerTexts(sb, enemyNode?["powers"], "    ");
            }
            var allyNodes = combatNode?["allies"] as JsonArray;
            int k = 0;
            for (int a = 1; a < cs.Allies.Count; a++)
            {
                if (cs.Allies[a] is MonsterState pet && pet.IsAlive)
                {
                    var allyNode = allyNodes != null && k < allyNodes.Count ? allyNodes[k] : null;
                    k++;
                    sb.Append(" ally [").Append(I(a)).Append("] ").Append(Describe.Uid(pet)).Append(' ').Append(pet.MonsterId);
                    AppendName(sb, allyNode);
                    sb.Append(' ').Append(I(pet.CurrentHp)).Append('/').Append(I(pet.MaxHp));
                    if (pet.Block > 0)
                    {
                        sb.Append(" block ").Append(I(pet.Block));
                    }
                    if (allyNode?["powers"] is JsonArray { Count: > 0 } allyPowers)
                    {
                        sb.Append(" | ").Append(PowersSummary(allyPowers));
                    }
                    sb.Append('\n');
                    AppendPowerTexts(sb, allyNode?["powers"], "    ");
                }
            }
            var player = cs.Player;
            sb.Append("-- hand (draw ").Append(I(player.DrawPile.Count)).Append(", discard ").Append(I(player.DiscardPile.Count)).Append(", exhaust ").Append(I(player.ExhaustPile.Count)).Append(")\n");
            var handNodes = combatNode?["hand"] as JsonArray;
            for (int i = 0; i < player.Hand.Count; i++)
            {
                var card = player.Hand[i];
                var cardNode = handNodes != null && i < handNodes.Count ? handNodes[i] : null;
                sb.Append(" [").Append(I(i)).Append("] ").Append(Describe.CardName(card));
                AppendName(sb, cardNode);
                sb.Append(' ').Append(Describe.CostText(cs, card));
                if (cardNode?["base_cost"] is JsonValue baseCost)
                {
                    sb.Append(" (base ").Append(baseCost.ToJsonString()).Append("e)");
                }
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
                sb.Append(EffectiveText(card, cardNode?["effective"]));
                sb.Append(CardTags(cardNode));
                var keywords = Describe.Keywords(cs, card);
                if (keywords.Count > 0)
                {
                    sb.Append(" {").Append(string.Join(",", keywords)).Append('}');
                }
                if (!Describe.Playable(cs, card, out var reason))
                {
                    sb.Append(" UNPLAYABLE:").Append(reason);
                }
                AppendText(sb, cardNode).Append('\n');
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
            sb.Append("-- prompt ").Append(prompt["kind"]).Append(" at ").Append(prompt["site"]);
            if (Str(prompt, "source") is { } source && source != Str(prompt, "site"))
            {
                sb.Append(" from ").Append(source);
            }
            AppendName(sb, prompt, "source_name");
            sb.Append(": pick ").Append(prompt["min"]!.ToJsonString()).Append("..").Append(prompt["max"]!.ToJsonString())
                .Append(", picked [").Append(string.Join(",", ((JsonArray)prompt["picked"]!).Select(n => n!.ToJsonString()))).Append("]\n");
            if (Str(prompt, "desc") is { } promptDesc)
            {
                sb.Append("   ").Append(promptDesc).Append('\n');
            }
            if (Str(prompt, "source_text") is { } sourceText)
            {
                sb.Append("   source: ").Append(Collapse(sourceText)).Append('\n');
            }
            foreach (var o in (JsonArray)prompt["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["id"]).Append(o["upgraded"]!.GetValue<bool>() ? "+" : string.Empty);
                AppendName(sb, o);
                AppendText(sb, o).Append('\n');
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
            AppendMapGrid(sb, map["nodes"] as JsonArray);
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
            bool canSkip = cr["can_skip"]!.GetValue<bool>();
            string skipNote = !canSkip ? string.Empty
                : cr["skip_consumes"] is JsonValue sc && sc.GetValue<bool>() ? " or skip (skipping completes the reward)"
                : " or skip (skip keeps this reward on the rewards screen until you proceed)";
            sb.Append("-- card reward (pick one").Append(skipNote).Append(")\n");
            foreach (var c in (JsonArray)cr["cards"]!)
            {
                sb.Append(" (").Append(c!["index"]!.ToJsonString()).Append(") ").Append(c["id"]).Append(c["upgraded"]!.GetValue<bool>() ? "+" : string.Empty);
                AppendName(sb, c);
                sb.Append(' ').Append(c["type"]).Append(' ').Append(c["rarity"]).Append(" cost ").Append(c["cost"]!.ToJsonString().Trim('"'));
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
                AppendText(sb, c).Append('\n');
            }
        }
        if (obs["shop"] is JsonObject shop)
        {
            sb.Append("-- shop (card removal: ").Append(shop["removal_price"] is { } rp ? rp.ToJsonString() + "g" : "n/a").Append(")\n");
            foreach (var e in (JsonArray)shop["entries"]!)
            {
                sb.Append(" (").Append(e!["index"]!.ToJsonString()).Append(") ").Append(e["category"]).Append(' ').Append(e["id"]?.GetValue<string>() ?? "-");
                AppendName(sb, e);
                sb.Append(' ').Append(e["price"]!.ToJsonString()).Append('g');
                if (e["on_sale"] is JsonValue sale && sale.GetValue<bool>())
                {
                    sb.Append(" (sale").Append(e["base_price"] is JsonValue bp ? ", base ~" + bp.ToJsonString() + "g" : string.Empty).Append(')');
                }
                sb.Append(e["sold"]!.GetValue<bool>() ? " SOLD" : string.Empty);
                AppendText(sb, e).Append('\n');
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
                AppendName(sb, t, "relic_name");
                AppendText(sb, t, "relic_text");
            }
            sb.Append('\n');
        }
        if (obs["event"] is JsonObject ev)
        {
            sb.Append("-- event ").Append(ev["id"]);
            AppendName(sb, ev);
            sb.Append(" page ").Append(ev["page"]?.GetValue<string>() ?? "-").Append(ev["finished"]!.GetValue<bool>() ? " (finished)" : string.Empty).Append('\n');
            foreach (var o in (JsonArray)ev["options"]!)
            {
                sb.Append(" (").Append(o!["index"]!.ToJsonString()).Append(") ").Append(o["key"]).Append(o["enabled"]!.GetValue<bool>() ? string.Empty : " (disabled)");
                if (o["relic"] is { } r)
                {
                    sb.Append(" relic ").Append(r.GetValue<string>());
                }
                AppendText(sb, o).Append('\n');
            }
        }
    }
}
