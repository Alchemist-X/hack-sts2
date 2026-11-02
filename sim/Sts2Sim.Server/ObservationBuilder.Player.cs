using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
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
                powers.Add(new JsonObject { ["id"] = id, ["amount"] = amount, ["text"] = Rt(() => RulesText.PowerText(id)) });
            }
        }
        node["powers"] = powers;
        var relics = new JsonArray();
        foreach (var relic in p.Relics)
        {
            var r = new JsonObject { ["id"] = relic.Id, ["name"] = Rt(() => RulesText.RelicTitle(relic.Id)), ["text"] = Rt(() => RulesText.RelicText(relic)) };
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
            potions.Add(potion == null ? null : new JsonObject { ["slot"] = i, ["id"] = potion.Id, ["name"] = Rt(() => RulesText.PotionTitle(potion.Id)), ["text"] = Rt(() => RulesText.PotionText(potion.Id)) });
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
}
