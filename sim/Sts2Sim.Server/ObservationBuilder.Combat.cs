using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
    // Visible powers of a creature: {id, amount, text, detail}; detail = target / applier / power-specific state.
    private static JsonArray PowerNodes(CombatState cs, CreatureState creature)
    {
        var powers = new JsonArray();
        foreach (var power in Describe.PowerModels(cs, creature))
        {
            string id = power.Id;
            powers.Add(new JsonObject
            {
                ["id"] = id,
                ["amount"] = power.Amount,
                ["text"] = Rt(() => RulesText.PowerText(id, power.Amount)),
                ["detail"] = Describe.PowerDetail(cs, power)
            });
        }
        return powers;
    }

    // "ID amount [detail], ..." from power nodes.
    private static string PowersSummary(JsonArray powers)
        => string.Join(", ", powers.Select(pw =>
        {
            string s = pw!["id"]!.GetValue<string>() + " " + pw["amount"]!.ToJsonString();
            return Str(pw, "detail") is { } detail ? s + " [" + detail + "]" : s;
        }));

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
            string? move = Describe.MoveId(m);
            string? moveName = move == null ? null : Rt(() => RulesText.EnemyMoveTitle(m.MonsterId, move));
            foreach (var intent in Describe.Intents(cs, m))
            {
                var it = new JsonObject { ["type"] = intent.Type, ["move"] = move, ["move_name"] = moveName };
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
            enemies.Add(new JsonObject
            {
                ["index"] = index,
                ["uid"] = Describe.Uid(m),
                ["id"] = m.MonsterId,
                ["name"] = Rt(() => RulesText.EnemyTitle(m.MonsterId)),
                ["hp"] = m.CurrentHp,
                ["max_hp"] = m.MaxHp,
                ["block"] = m.Block,
                ["flank"] = Describe.Flank(m),
                ["behind"] = Describe.Behind(cs, m),
                ["intents"] = intents,
                ["powers"] = PowerNodes(cs, m)
            });
        }
        var allies = new JsonArray();
        for (int a = 1; a < cs.Allies.Count; a++)
        {
            var ally = cs.Allies[a];
            if (ally is MonsterState pet && pet.IsAlive)
            {
                allies.Add(new JsonObject
                {
                    ["index"] = a,
                    ["uid"] = Describe.Uid(pet),
                    ["id"] = pet.MonsterId,
                    ["name"] = Rt(() => RulesText.EnemyTitle(pet.MonsterId)),
                    ["hp"] = pet.CurrentHp,
                    ["max_hp"] = pet.MaxHp,
                    ["block"] = pet.Block,
                    ["powers"] = PowerNodes(cs, pet)
                });
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
            ["enemies"] = enemies,
            ["danger"] = DangerNode(cs)
        };
        if (allies.Count > 0)
        {
            node["allies"] = allies;
        }
        return node;
    }

    private static JsonObject? DangerNode(CombatState cs)
    {
        Describe.Danger d;
        try
        {
            d = Describe.DangerOf(cs);
        }
        catch (Exception)
        {
            return null;
        }
        var by = new JsonArray();
        foreach (var e in d.ByEnemy)
        {
            by.Add(new JsonObject { ["uid"] = e.Uid, ["id"] = e.MonsterId, ["damage"] = e.Damage, ["times"] = e.Times });
        }
        var timers = new JsonArray();
        foreach (var t in d.Timers)
        {
            timers.Add(new JsonObject { ["source"] = t.Source, ["id"] = t.Id, ["text"] = t.Text });
        }
        return new JsonObject
        {
            ["incoming"] = d.Total,
            ["hits"] = d.Hits,
            ["by_enemy"] = by,
            ["block"] = d.Block,
            ["net_hp_loss"] = d.NetHpLoss,
            ["hp_after"] = d.HpAfter,
            ["timers"] = timers
        };
    }

    // "incoming: 18 dmg in 3 hits (u2 12x1, u3 3x2), block 5, net -13 HP -> 42/80" plus one line per timer.
    private static void AppendDanger(StringBuilder sb, JsonObject? danger, int maxHp)
    {
        if (danger == null)
        {
            return;
        }
        int incoming = danger["incoming"]!.GetValue<int>();
        int block = danger["block"]!.GetValue<int>();
        sb.Append("incoming: ");
        if (incoming == 0)
        {
            sb.Append("no attack, block ").Append(I(block));
        }
        else
        {
            var parts = ((JsonArray)danger["by_enemy"]!).Select(e => $"{e!["uid"]} {e["damage"]!.ToJsonString()}x{e["times"]!.ToJsonString()}");
            int hits = danger["hits"]!.GetValue<int>();
            sb.Append(I(incoming)).Append(" dmg in ").Append(I(hits)).Append(hits == 1 ? " hit (" : " hits (").Append(string.Join(", ", parts)).Append("), block ").Append(I(block))
                .Append(", net -").Append(danger["net_hp_loss"]!.ToJsonString()).Append(" HP -> ").Append(danger["hp_after"]!.ToJsonString()).Append('/').Append(I(maxHp));
        }
        sb.Append('\n');
        foreach (var t in (JsonArray)danger["timers"]!)
        {
            sb.Append("! ").Append(t!["text"]!.GetValue<string>()).Append('\n');
        }
    }
}
