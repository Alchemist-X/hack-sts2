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
            var option = new JsonObject { ["index"] = i, ["id"] = o.ModelId, ["name"] = name, ["upgraded"] = o.UpgradeLevel > 0, ["text"] = text };
            if (p.Kind is ChoicePromptKind.HandUpgrade or ChoicePromptKind.DeckUpgrade && CardRegistry.TryGet(o.ModelId, out _))
            {
                // The upgrade screen previews the upgraded card (NDeckUpgradeSelectScreen _singlePreview / NUpgradePreview):
                // its rules text at the next upgrade level (base vars of that level).
                int next = o.UpgradeLevel + 1;
                option["upgrade_text"] = Rt(() => RulesText.CardText(o.ModelId, next));
            }
            options.Add(option);
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
        var (sourceName, sourceText) = PromptSourceNameText(g, p);
        return new JsonObject
        {
            ["kind"] = p.Kind.ToString(),
            ["site"] = p.SiteId,
            ["min"] = p.MinSelect,
            ["max"] = p.MaxSelect,
            ["picked"] = picked,
            ["options"] = options,
            ["can_confirm"] = canConfirm,
            ["can_cancel"] = p.Cancelable,
            ["source"] = p.SourceId,
            ["source_name"] = sourceName,
            ["source_text"] = sourceText,
            ["pile"] = p.Pile?.ToString(),
            ["effect"] = PromptEffect(p),
            ["needs_confirm"] = p.RequireConfirm,
            ["auto_completes_at"] = p.RequireConfirm ? null : p.MaxSelect,
            ["desc"] = PromptDesc(p, picks.Count)
        };
    }

    // What the selection does, from the prompt kind and source pile (the source's own rules text,
    // source_text, says exactly what happens to the picked cards).
    private static string PromptEffect(ChoicePrompt p)
    {
        string pile = p.Pile is { } pt ? pt.ToString() : "?";
        return p.Kind switch
        {
            ChoicePromptKind.HandSelect => "select cards in your Hand",
            ChoicePromptKind.HandUpgrade => "Upgrade the picked cards in your Hand",
            ChoicePromptKind.PileSelect => "select cards from your " + pile + " pile",
            ChoicePromptKind.ChooseCard => "choose a card from the offered ones",
            ChoicePromptKind.SimpleGrid => "choose from the offered options",
            ChoicePromptKind.DeckUpgrade => "Upgrade the picked cards in your Deck",
            ChoicePromptKind.DeckTransform => "Transform the picked cards in your Deck",
            ChoicePromptKind.DeckEnchant => "Enchant the picked cards in your Deck",
            ChoicePromptKind.DeckRemove => "remove the picked cards from your Deck",
            ChoicePromptKind.DeckGeneric => "select cards in your Deck",
            ChoicePromptKind.RewardsGrid => "choose rewards",
            ChoicePromptKind.Bundle => "choose a bundle (all its cards are added to your Deck)",
            ChoicePromptKind.CardReward => "choose a card to add to your Deck",
            _ => p.Kind.ToString()
        };
    }

    // How the prompt settles: whether a 'pick' resolves it at once, on the Nth pick, or only on 'confirm'.
    private static string PromptDesc(ChoicePrompt p, int picked)
    {
        string range = p.MinSelect == p.MaxSelect ? I(p.MaxSelect) : I(p.MinSelect) + ".." + I(p.MaxSelect);
        string settle;
        if (p.RequireConfirm)
        {
            settle = "pick " + range + (p.MaxSelect > 1 ? " (any order)" : string.Empty) + ", then 'confirm'";
        }
        else if (p.MaxSelect == 1)
        {
            settle = "a single 'pick' settles it immediately";
            if (p.CanSkip && p.MinSelect == 0)
            {
                settle += " ('confirm' with no pick skips)";
            }
        }
        else
        {
            settle = "pick in any order; pick #" + I(p.MaxSelect) + " settles it automatically";
            if (p.CanSkip)
            {
                settle += "; 'confirm' stops early with " + I(p.MinSelect) + "+ picks";
            }
        }
        if (p.Cancelable)
        {
            settle += "; 'cancel' backs out";
        }
        return PromptEffect(p) + ": " + settle + " (" + I(picked) + " picked)";
    }

    // Name and rules text of what opened the prompt (a card being played, a relic, a potion, an event).
    private static (string? Name, string? Text) PromptSourceNameText(GameSession g, ChoicePrompt p)
    {
        string id = p.SourceId;
        if (string.IsNullOrEmpty(id))
        {
            return (null, null);
        }
        if (FindCard(g.Player, p.SourceInstanceId, id) is { } card)
        {
            return (Rt(() => RulesText.CardTitle(card.Id)), Rt(() => RulesText.CardText(card)));
        }
        if (CardRegistry.TryGet(id, out _))
        {
            return (Rt(() => RulesText.CardTitle(id)), Rt(() => RulesText.CardText(id, 0)));
        }
        foreach (var relic in g.Player.Relics)
        {
            if (relic.Id == id)
            {
                return (Rt(() => RulesText.RelicTitle(id)), Rt(() => RulesText.RelicText(relic)));
            }
        }
        if (Rt(() => RulesText.RelicTitle(id)) is { } relicTitle)
        {
            return (relicTitle, Rt(() => RulesText.RelicText(id)));
        }
        if (Rt(() => RulesText.PotionTitle(id)) is { } potionTitle)
        {
            return (potionTitle, Rt(() => RulesText.PotionText(id)));
        }
        return (Rt(() => RulesText.EventTitle(id)), null);
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
            int upgrade = o.UpgradeLevel;
            return (Rt(() => RulesText.CardTitle(id)), Rt(() => RulesText.CardText(id, upgrade)));
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
