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
        node["text"] = Rt(() => RulesText.CardText(card));
        if (withPlayability && cs != null)
        {
            bool ok = Describe.Playable(cs, card, out var reason);
            node["playable"] = ok;
            node["reason"] = reason;
        }
        return node;
    }
}
