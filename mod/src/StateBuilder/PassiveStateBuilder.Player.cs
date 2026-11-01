// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs (player/card sections).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations: PowerModel.TypeForCurrentAmount is used for the buff/debuff
// tag (reflects the CURRENT amount, e.g. negative Strength reads as Debuff; PowerModel.Type
// exists too but is the static declaration). Draw pile stays SORTED (rarity, then id):
// true draw order is hidden information and never appears in the observation channel.
// Additions (docs/hook-map.md, v0.111.0 addendum): master deck in deck order (R3; the
// deck view's default sort is this order) and relic saved properties (R7).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static Dictionary<string, object?> BuildPlayerState(Player player, bool includeDeck = false)
    {
        var state = new Dictionary<string, object?>();
        var creature = player.Creature;
        var combatState = player.PlayerCombatState;

        state["character"] = SafeGetText(() => player.Character.Title);
        state["hp"] = creature.CurrentHp;
        state["max_hp"] = creature.MaxHp;
        state["block"] = creature.Block;

        // PlayerCombatState can linger after combat while on map/rest/shop. Energy/MaxEnergy getters
        // run hooks (e.g. Hook.ModifyMaxEnergy) that null-ref without a live combat - only serialize
        // combat fields when a fight is actually in progress.
        if (combatState != null && CombatManager.Instance.IsInProgress)
        {
            state["energy"] = combatState.Energy;
            state["max_energy"] = combatState.MaxEnergy;

            // Stars (The Regent's resource, conditionally shown)
            if (player.Character.ShouldAlwaysShowStarCounter || combatState.Stars > 0)
            {
                state["stars"] = combatState.Stars;
            }

            // Hand
            var hand = new List<Dictionary<string, object?>>();
            int cardIndex = 0;
            foreach (var card in combatState.Hand.Cards)
            {
                hand.Add(BuildCardState(card, cardIndex));
                cardIndex++;
            }
            state["hand"] = hand;

            // Pile counts
            state["draw_pile_count"] = combatState.DrawPile.Cards.Count;
            state["discard_pile_count"] = combatState.DiscardPile.Cards.Count;
            state["exhaust_pile_count"] = combatState.ExhaustPile.Cards.Count;

            // Pile contents. Draw pile is deliberately sorted by rarity then card ID
            // (matching the in-game display) so the true draw ORDER - hidden info -
            // never leaks into the observation channel.
            var drawCards = combatState.DrawPile.Cards.ToList();
            drawCards.Sort((c1, c2) => c1.Rarity != c2.Rarity
                ? c1.Rarity.CompareTo(c2.Rarity)
                : string.Compare(c1.Id.Entry, c2.Id.Entry, StringComparison.Ordinal));
            state["draw_pile"] = BuildPileCardList(drawCards, PileType.Draw);
            state["discard_pile"] = BuildPileCardList(combatState.DiscardPile.Cards, PileType.Discard);
            state["exhaust_pile"] = BuildPileCardList(combatState.ExhaustPile.Cards, PileType.Exhaust);

            // Orbs
            var orbQueue = combatState.OrbQueue;
            if (orbQueue != null && orbQueue.Capacity > 0)
            {
                var orbs = new List<Dictionary<string, object?>>();
                foreach (var orb in orbQueue.Orbs)
                {
                    // Populate SmartDescription placeholders with Focus-modified values,
                    // mirroring OrbModel.HoverTips getter (OrbModel.cs)
                    string? description = SafeGetText(() =>
                    {
                        var desc = orb.SmartDescription;
                        desc.Add("energyPrefix", orb.Owner.Character.CardPool.Title);
                        desc.Add("Passive", orb.PassiveVal);
                        desc.Add("Evoke", orb.EvokeVal);
                        return desc;
                    });
                    orbs.Add(new Dictionary<string, object?>
                    {
                        ["id"] = orb.Id.Entry,
                        ["name"] = SafeGetText(() => orb.Title),
                        ["description"] = description,
                        ["passive_val"] = orb.PassiveVal,
                        ["evoke_val"] = orb.EvokeVal,
                        ["keywords"] = BuildHoverTips(orb.HoverTips)
                    });
                }
                state["orbs"] = orbs;
                state["orb_slots"] = orbQueue.Capacity;
                state["orb_empty_slots"] = orbQueue.Capacity - orbQueue.Orbs.Count;
            }

            // Pets (Osty for Necrobinder)
            var pets = BuildPetsState(player);
            if (pets.Count > 0)
            {
                state["pets"] = pets;
            }
        }

        if (includeDeck || !CombatManager.Instance.IsInProgress)
        {
            state["deck"] = BuildDeckState(player);
        }

        state["gold"] = player.Gold;

        // Powers (status effects)
        state["status"] = BuildPowersState(creature);

        // Relics
        var relics = new List<Dictionary<string, object?>>();
        foreach (var relic in player.Relics)
        {
            var relicState = new Dictionary<string, object?>
            {
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["counter"] = relic.ShowCounter ? relic.DisplayAmount : null,
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            };
            var props = BuildSavedProps(relic);
            if (props != null)
                relicState["props"] = props;
            relics.Add(relicState);
        }
        state["relics"] = relics;

        // Potions
        var potions = new List<Dictionary<string, object?>>();
        int slotIndex = 0;
        foreach (var potion in player.PotionSlots)
        {
            if (potion != null)
            {
                potions.Add(new Dictionary<string, object?>
                {
                    ["id"] = potion.Id.Entry,
                    ["name"] = SafeGetText(() => potion.Title),
                    ["description"] = SafeGetText(() => potion.DynamicDescription),
                    ["slot"] = slotIndex,
                    ["can_use_in_combat"] = potion.Usage == PotionUsage.CombatOnly || potion.Usage == PotionUsage.AnyTime,
                    ["target_type"] = potion.TargetType.ToString(),
                    ["keywords"] = BuildHoverTips(potion.ExtraHoverTips)
                });
            }
            slotIndex++;
        }
        state["potions"] = potions;
        state["max_potion_slots"] = player.MaxPotionCount;

        return state;
    }

    /// <summary>R3: the master deck in deck (obtained) order.</summary>
    private static object BuildDeckState(Player player)
    {
        try
        {
            var deck = new List<Dictionary<string, object?>>();
            foreach (var card in player.Deck.Cards)
                deck.Add(BuildCardIdentity(card));
            return deck;
        }
        catch (Exception ex)
        {
            return SectionError(ex);
        }
    }

    /// <summary>{id, upgrade_level, enchantment {id, amount} | null}.</summary>
    private static Dictionary<string, object?> BuildCardIdentity(CardModel card)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = card.Id.Entry,
            ["upgrade_level"] = card.CurrentUpgradeLevel,
            ["enchantment"] = BuildEnchantmentState(card)
        };
    }

    private static Dictionary<string, object?>? BuildEnchantmentState(CardModel card)
    {
        var enchantment = card.Enchantment;
        if (enchantment == null) return null;
        return new Dictionary<string, object?>
        {
            ["id"] = enchantment.Id.Entry,
            ["amount"] = enchantment.Amount
        };
    }

    /// <summary>
    /// R7: the model's [SavedProperty] values grouped like the run save's "props"
    /// ({ints|bools|strings|int_arrays|model_ids|cards|card_arrays: [{name, value}]}),
    /// read through the game's prebuilt property list
    /// (ModelIdSerializationCache.GetJsonPropertiesForType, a pure lookup). Unlike the
    /// save, default values are kept (SavedProperties.From is not called: its default
    /// check instantiates models into a game-side cache). Null when the model has none.
    /// </summary>
    private static JsonObject? BuildSavedProps(AbstractModel model)
    {
        try
        {
            var properties = ModelIdSerializationCache.GetJsonPropertiesForType(model.GetType());
            if (properties == null || properties.Count == 0) return null;
            var groups = new Dictionary<string, JsonArray>();
            foreach (var property in properties)
            {
                string? group;
                JsonNode? node;
                try
                {
                    var value = property.GetValue(model);
                    if (value == null) continue;
                    (group, node) = SavedPropValue(value);
                }
                catch { continue; }
                if (group == null) continue;
                if (!groups.TryGetValue(group, out var array))
                {
                    array = new JsonArray();
                    groups[group] = array;
                }
                array.Add(new JsonObject { ["name"] = property.Name, ["value"] = node });
            }
            if (groups.Count == 0) return null;
            var result = new JsonObject();
            foreach (var group in SavedPropGroups)
            {
                if (groups.TryGetValue(group, out var array))
                    result[group] = array;
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static readonly string[] SavedPropGroups =
        { "ints", "bools", "strings", "int_arrays", "model_ids", "cards", "card_arrays" };

    /// <summary>Same type dispatch as SavedProperties.FromInternal (Saves.Runs/SavedProperties.cs:63).</summary>
    private static (string? Group, JsonNode? Node) SavedPropValue(object value)
    {
        switch (value)
        {
            case int i:
                return ("ints", JsonValue.Create(i));
            case int[] ints:
                return ("int_arrays", new JsonArray(ints.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()));
            case Enum e:
                return ("ints", JsonValue.Create(Convert.ToInt32(e)));
            case Enum[] enums:
                return ("int_arrays", new JsonArray(enums.Select(v => (JsonNode?)JsonValue.Create(Convert.ToInt32(v))).ToArray()));
            case ModelId id:
                return ("model_ids", JsonValue.Create(id.ToString()));
            case bool b:
                return ("bools", JsonValue.Create(b));
            case string str:
                return ("strings", JsonValue.Create(str));
            case SerializableCard card:
                return ("cards", SerializableCardNode(card));
            case List<SerializableCard> cards:
                return ("card_arrays", new JsonArray(cards.Select(SerializableCardNode).ToArray()));
            default:
                return (null, null);
        }
    }

    private static JsonNode? SerializableCardNode(SerializableCard? card)
    {
        if (card == null) return null;
        var node = new JsonObject
        {
            ["id"] = card.Id?.ToString(),
            ["current_upgrade_level"] = card.CurrentUpgradeLevel
        };
        if (card.Enchantment != null)
        {
            node["enchantment"] = new JsonObject
            {
                ["id"] = card.Enchantment.Id?.ToString(),
                ["amount"] = card.Enchantment.Amount
            };
        }
        return node;
    }

    private static string GetCostDisplay(CardModel card)
        => card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString();

    private static string? GetStarCostDisplay(CardModel card)
    {
        if (card.HasStarCostX) return "X";
        if (card.CurrentStarCost >= 0) return card.GetStarCostWithModifiers().ToString();
        return null;
    }

    /// <summary>
    /// Builds the common card display fields shared across all card serialization contexts.
    /// Callers merge context-specific fields (e.g. index, can_play, target_type) on top.
    /// </summary>
    private static Dictionary<string, object?> BuildCardInfo(CardModel card, PileType pile = PileType.None)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = card.Id.Entry,
            ["name"] = SafeGetText(() => card.Title),
            ["type"] = card.Type.ToString(),
            ["cost"] = GetCostDisplay(card),
            ["star_cost"] = GetStarCostDisplay(card),
            ["description"] = SafeGetCardDescription(card, pile),
            ["rarity"] = card.Rarity.ToString(),
            ["is_upgraded"] = card.IsUpgraded,
            ["keywords"] = BuildHoverTips(card.HoverTips)
        };
    }

    private static Dictionary<string, object?> BuildCardState(CardModel card, int index)
    {
        card.CanPlay(out var unplayableReason, out _);

        var state = BuildCardInfo(card);
        state["index"] = index;
        state["description"] = SafeGetCardDescription(card); // hand cards use default pile
        state["target_type"] = card.TargetType.ToString();
        state["can_play"] = unplayableReason == UnplayableReason.None;
        state["unplayable_reason"] = unplayableReason != UnplayableReason.None ? unplayableReason.ToString() : null;
        return state;
    }

    private static void AddPreviewCardsFromContainer(
        Godot.Control? container,
        List<Dictionary<string, object?>> previewCards)
    {
        if (container?.Visible != true)
            return;

        var cardHolders = FindAllSortedByPosition<NCardHolder>(container);
        if (cardHolders.Count > 0)
        {
            foreach (var holder in cardHolders)
            {
                var card = holder.CardModel;
                if (card == null) continue;

                var cardInfo = BuildCardInfo(card);
                cardInfo["index"] = previewCards.Count;
                previewCards.Add(cardInfo);
            }
            return;
        }

        foreach (var holder in FindAll<NPreviewCardHolder>(container))
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = previewCards.Count;
            previewCards.Add(cardInfo);
        }
    }

    private static List<Dictionary<string, object?>> BuildPileCardList(IEnumerable<CardModel> cards, PileType pile)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var card in cards)
        {
            // Pile cards only need a subset - keep it lightweight
            list.Add(new Dictionary<string, object?>
            {
                ["name"] = SafeGetText(() => card.Title),
                ["cost"] = GetCostDisplay(card),
                ["star_cost"] = GetStarCostDisplay(card),
                ["description"] = SafeGetCardDescription(card, pile)
            });
        }
        return list;
    }

    private static List<Dictionary<string, object?>> BuildPowersState(Creature creature)
    {
        var powers = new List<Dictionary<string, object?>>();
        foreach (var power in creature.Powers)
        {
            if (!power.IsVisible) continue;

            // Per-power try/catch: HoverTips getter calls into game engine code
            // (LocString resolution, DynamicVars, virtual ExtraHoverTips) that can
            // throw during state transitions. Skip the power rather than fail the
            // entire state query.
            try
            {
                var allTips = power.HoverTips.ToList();
                string? resolvedDesc = null;
                var extraTips = new List<IHoverTip>();
                foreach (var tip in allTips)
                {
                    if (tip.Id == power.Id.ToString())
                    {
                        if (tip is HoverTip ht && ht.Description != null)
                            resolvedDesc = StripRichTextTags(ht.Description);
                    }
                    else
                    {
                        extraTips.Add(tip);
                    }
                }
                resolvedDesc ??= SafeGetText(() => power.SmartDescription);

                powers.Add(new Dictionary<string, object?>
                {
                    ["id"] = power.Id.Entry,
                    ["name"] = SafeGetText(() => power.Title),
                    ["amount"] = power.DisplayAmount,
                    // TypeForCurrentAmount = Buff/Debuff for the CURRENT amount (e.g.
                    // negative Strength reads as Debuff), unlike the static PowerModel.Type.
                    ["type"] = power.TypeForCurrentAmount.ToString(),
                    ["description"] = resolvedDesc,
                    ["keywords"] = BuildHoverTips(extraTips)
                });
            }
            catch { /* skip this power - game engine state may be inconsistent */ }
        }
        return powers;
    }

    private static List<Dictionary<string, object?>> BuildPetsState(Player player)
    {
        var pets = new List<Dictionary<string, object?>>();
        var combatState = player.PlayerCombatState;
        if (combatState == null) return pets;

        // Check Osty specifically (Byrdpip/PaelsLegion are cosmetic with no real combat state)
        var osty = combatState.GetPet<Osty>();
        if (osty != null)
        {
            pets.Add(new Dictionary<string, object?>
            {
                ["id"] = osty.Monster?.Id.Entry ?? "OSTY",
                ["name"] = SafeGetText(() => osty.Monster?.Title) ?? "Osty",
                ["alive"] = osty.IsAlive,
                ["hp"] = osty.CurrentHp,
                ["max_hp"] = osty.MaxHp,
                ["block"] = osty.Block,
                ["status"] = BuildPowersState(osty)
            });
        }

        return pets;
    }
}
