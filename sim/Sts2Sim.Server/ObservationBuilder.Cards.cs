using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
    private static JsonObject CardNode(CombatState? cs, CardInstance card, int index, bool withPlayability)
    {
        var node = new JsonObject
        {
            ["index"] = index,
            ["id"] = card.Id,
            ["name"] = Rt(() => RulesText.CardTitle(card.Id)),
            ["upgraded"] = card.IsUpgraded,
            ["cost"] = Describe.CostNode(cs, card),
            ["base_cost"] = Describe.BaseCost(cs, card)
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
        node["effective"] = withPlayability && cs != null ? EffectiveNode(cs, card) : null;
        node["text"] = Rt(() => RulesText.CardText(card));
        var enchantments = new JsonArray();
        if (card.Enchantment is { } ench)
        {
            enchantments.Add(new JsonObject { ["id"] = ench.Id, ["amount"] = ench.Amount });
        }
        node["enchantments"] = enchantments;
        node["affliction"] = card.Affliction is { } aff ? new JsonObject { ["id"] = aff.Id, ["amount"] = aff.Amount } : null;
        node["dampened"] = cs != null && Describe.DampenedCardIds(cs).Contains(card.InstanceId);
        if (withPlayability && cs != null)
        {
            bool ok = Describe.Playable(cs, card, out var reason);
            node["playable"] = ok;
            node["reason"] = reason;
        }
        return node;
    }

    // GET /games/{id}/piles. draw is sorted by id (then upgrade) so the draw order stays hidden; discard and exhaust keep
    // pile order; deck is the master deck. Combat piles are null outside a combat.
    public static JsonObject PilesNode(GameSession g)
    {
        var cs = g.LiveCombat;
        var p = g.Player;
        JsonArray Nodes(IEnumerable<CardInstance> cards)
        {
            var arr = new JsonArray();
            int i = 0;
            foreach (var card in cards)
            {
                arr.Add(CardNode(cs, card, i++, false));
            }
            return arr;
        }
        return new JsonObject
        {
            ["game_id"] = g.Id,
            ["in_combat"] = cs != null,
            ["draw"] = cs != null ? Nodes(cs.Player.DrawPile.Cards.OrderBy(c => c.Id, StringComparer.Ordinal).ThenBy(c => c.UpgradeLevel)) : null,
            ["discard"] = cs != null ? Nodes(cs.Player.DiscardPile.Cards) : null,
            ["exhaust"] = cs != null ? Nodes(cs.Player.ExhaustPile.Cards) : null,
            ["hand"] = cs != null ? Nodes(cs.Player.Hand.Cards) : null,
            ["deck"] = Nodes(p.Deck)
        };
    }

    // {"damage": {uid: per-hit damage} | null, "times": n | null, "block": n | null}, or null when the card has neither.
    private static JsonObject? EffectiveNode(CombatState cs, CardInstance card)
    {
        if (Describe.EffectiveNumbers(cs, card) is not { } eff)
        {
            return null;
        }
        JsonObject? damage = null;
        if (eff.Damage != null)
        {
            damage = new JsonObject();
            foreach (var (uid, value) in eff.Damage)
            {
                damage[uid] = value;
            }
        }
        return new JsonObject { ["damage"] = damage, ["times"] = eff.Times, ["block"] = eff.Block };
    }

    // " eff 22 vs u2, 15 vs u3" / " eff 9" / " eff 8 block": only the parts that differ from the base vars (or are calculated).
    private static string EffectiveText(CardInstance card, JsonNode? effective)
    {
        if (effective is not JsonObject eff)
        {
            return string.Empty;
        }
        var vars = Describe.Vars(card);
        var parts = new List<string>();
        if (eff["damage"] is JsonObject dmg && dmg.Count > 0)
        {
            int? baseDamage = vars.TryGetValue("damage", out int bd) ? bd : vars.TryGetValue("osty_damage", out int od) ? od : null;
            var values = dmg.Select(kv => (kv.Key, kv.Value!.GetValue<int>())).ToList();
            int times = eff["times"] is JsonValue t ? t.GetValue<int>() : 1;
            bool differs = baseDamage == null || times > 1 || values.Any(v => v.Item2 != baseDamage);
            if (differs)
            {
                string hits = times > 1 ? " x" + I(times) : string.Empty;
                parts.Add(values.Select(v => v.Item2).Distinct().Count() == 1
                    ? I(values[0].Item2) + " dmg" + hits
                    : string.Join(", ", values.Select(v => $"{I(v.Item2)} vs {v.Key}")) + hits);
            }
        }
        if (eff["block"] is JsonValue blockNode)
        {
            int block = blockNode.GetValue<int>();
            int? baseBlock = vars.TryGetValue("block", out int bb) ? bb : null;
            if (baseBlock != block)
            {
                parts.Add(I(block) + " block");
            }
        }
        return parts.Count == 0 ? string.Empty : " (eff " + string.Join("; ", parts) + ")";
    }

    // " [BOUND]" / " [ench SHARP 2]" / " [dampened]" tags for the hand line.
    private static string CardTags(JsonNode? cardNode)
    {
        if (cardNode is not JsonObject node)
        {
            return string.Empty;
        }
        var sb = new StringBuilder();
        if (node["affliction"] is JsonObject aff)
        {
            sb.Append(" [").Append(aff["id"]!.GetValue<string>());
            int amount = aff["amount"]!.GetValue<int>();
            if (amount > 1)
            {
                sb.Append(' ').Append(I(amount));
            }
            sb.Append(']');
        }
        if (node["enchantments"] is JsonArray enchantments)
        {
            foreach (var e in enchantments)
            {
                sb.Append(" [ench ").Append(e!["id"]!.GetValue<string>()).Append(' ').Append(e["amount"]!.ToJsonString()).Append(']');
            }
        }
        if (node["dampened"] is JsonValue d && d.GetValue<bool>())
        {
            sb.Append(" [dampened]");
        }
        return sb.ToString();
    }
}
