using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

public static partial class ObservationBuilder
{
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
            var (name, text) = PromptOptionNameText(g, p, o);
            options.Add(new JsonObject { ["index"] = i, ["id"] = o.ModelId, ["name"] = name, ["upgraded"] = o.UpgradeLevel > 0, ["text"] = text });
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

    // Name and rules text of a prompt option: a card instance found in the player's piles or deck, else a card,
    // relic or potion id (in that order of registry lookup).
    private static (string? Name, string? Text) PromptOptionNameText(GameSession g, ChoicePrompt p, ChoiceOption o)
    {
        _ = p;
        string id = o.ModelId;
        if (FindCard(g.Player, o.InstanceId, id) is { } card)
        {
            return (Rt(() => RulesText.CardTitle(card.Id)), Rt(() => RulesText.CardText(card)));
        }
        if (CardRegistry.TryGet(id, out _))
        {
            return (Rt(() => RulesText.CardTitle(id)), null);
        }
        if (RelicRegistry.Models.Contains(id))
        {
            return (Rt(() => RulesText.RelicTitle(id)), Rt(() => RulesText.RelicText(id)));
        }
        if (PotionRegistry.Models.Contains(id))
        {
            return (Rt(() => RulesText.PotionTitle(id)), Rt(() => RulesText.PotionText(id)));
        }
        return (null, null);
    }

    private static CardInstance? FindCard(PlayerState player, int instanceId, string modelId)
    {
        try
        {
            foreach (var pile in player.AllPiles)
            {
                for (int i = 0; i < pile.Count; i++)
                {
                    if (pile[i].InstanceId == instanceId && pile[i].Id == modelId)
                    {
                        return pile[i];
                    }
                }
            }
            foreach (var card in player.Deck)
            {
                if (card.InstanceId == instanceId && card.Id == modelId)
                {
                    return card;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }
}
