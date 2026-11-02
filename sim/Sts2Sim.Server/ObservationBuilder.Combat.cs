using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
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
                var it = new JsonObject { ["type"] = intent.Type, ["move"] = null };
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
                powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount, ["text"] = Rt(() => RulesText.PowerText(id)) });
            }
            enemies.Add(new JsonObject
            {
                ["index"] = index,
                ["id"] = m.MonsterId,
                ["name"] = Rt(() => RulesText.EnemyTitle(m.MonsterId)),
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
                    powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount, ["text"] = Rt(() => RulesText.PowerText(id)) });
                }
                allies.Add(new JsonObject { ["index"] = a, ["id"] = pet.MonsterId, ["name"] = Rt(() => RulesText.EnemyTitle(pet.MonsterId)), ["hp"] = pet.CurrentHp, ["max_hp"] = pet.MaxHp, ["block"] = pet.Block, ["powers"] = powers });
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
}
