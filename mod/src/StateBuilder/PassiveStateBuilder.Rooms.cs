// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs (room sections).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations (game v0.107.1) — ALL UI side effects removed:
//   * Shop: upstream auto-called NMerchantRoom.OpenInventory(); we read the merchant model
//     inventory directly (v0.107.1: MerchantRoom.GetLocalInventory(), populated model-side
//     in MerchantRoom.Enter) and report inventory_open read-only.
//   * FakeMerchant: upstream ForceClick()ed the merchant button; we read FakeMerchant.Inventory
//     directly (populated model-side in BeforeEventStarted) and emit "not_opened" if absent.
//   * Treasure: upstream ForceClick()ed the chest; we read the chest button state and emit
//     {"relics": "not_opened"} until the player opens the chest (contents stay hidden info).

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static Dictionary<string, object?> BuildEventState(EventRoom eventRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var eventModel = eventRoom.CanonicalEvent;
        bool isAncient = eventModel is AncientEventModel;
        state["event_id"] = eventModel.Id.Entry;
        state["event_name"] = SafeGetText(() => eventModel.Title);
        state["is_ancient"] = isAncient;

        // Check dialogue state for ancients
        bool inDialogue = false;
        var uiRoom = NEventRoom.Instance;
        if (isAncient && uiRoom != null)
        {
            var ancientLayout = FindFirst<NAncientEventLayout>(uiRoom);
            if (ancientLayout != null)
            {
                var hitbox = ancientLayout.GetNodeOrNull<NClickableControl>("%DialogueHitbox");
                inDialogue = hitbox != null && hitbox.Visible && hitbox.IsEnabled;
            }
        }
        state["in_dialogue"] = inDialogue;

        // Event body text
        state["body"] = SafeGetText(() => eventModel.Description);

        // Options from UI (read-only)
        var options = new List<Dictionary<string, object?>>();
        if (uiRoom != null)
        {
            var buttons = FindAll<NEventOptionButton>(uiRoom);
            int index = 0;
            foreach (var button in buttons)
            {
                var opt = button.Option;
                var optData = new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["title"] = SafeGetText(() => opt.Title),
                    ["description"] = SafeGetText(() => opt.Description),
                    ["is_locked"] = opt.IsLocked,
                    ["is_proceed"] = opt.IsProceed,
                    ["was_chosen"] = opt.WasChosen
                };
                if (opt.Relic != null)
                {
                    optData["relic_name"] = SafeGetText(() => opt.Relic.Title);
                    optData["relic_description"] = SafeGetText(() => opt.Relic.DynamicDescription);
                }
                optData["keywords"] = BuildHoverTips(opt.HoverTips);
                options.Add(optData);
                index++;
            }
        }
        state["options"] = options;

        return state;
    }

    private static Dictionary<string, object?> BuildFakeMerchantState(EventRoom eventRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        // LocalMutableEvent holds the per-player mutable copy with populated inventory;
        // CanonicalEvent is the shared template which may not have it.
        var fakeMerchant = eventRoom.LocalMutableEvent as FakeMerchant
                           ?? (FakeMerchant)eventRoom.CanonicalEvent;

        state["event_id"] = fakeMerchant.Id.Entry;
        state["event_name"] = SafeGetText(() => fakeMerchant.Title);
        state["started_fight"] = fakeMerchant.StartedFight;

        // Find the NFakeMerchant UI node (read-only; used only for button states)
        var uiRoom = NEventRoom.Instance;
        NFakeMerchant? fakeMerchantNode = null;
        if (uiRoom != null)
            fakeMerchantNode = FindFirst<NFakeMerchant>(uiRoom);

        if (fakeMerchant.StartedFight)
        {
            // After the foul potion fight, merchant is gone - just show proceed
            state["shop"] = new Dictionary<string, object?>
            {
                ["items"] = new List<Dictionary<string, object?>>(),
                ["can_proceed"] = true
            };
            state["message"] = "The fake merchant has been defeated.";
            return state;
        }

        // Passive adaptation: upstream ForceClick()ed the merchant button here to open
        // the inventory UI. The FakeMerchant model inventory is populated in
        // BeforeEventStarted() (model-side), so we read it directly; the UI open state
        // is reported read-only.
        bool inventoryUiOpen = false;
        if (fakeMerchantNode != null)
        {
            var inventoryUI = FindFirst<NMerchantInventory>(fakeMerchantNode);
            inventoryUiOpen = inventoryUI?.IsOpen ?? false;
        }
        state["inventory_open"] = inventoryUiOpen;

        // Build shop inventory from the FakeMerchant model
        MerchantInventory? inventory = null;
        try { inventory = fakeMerchant.Inventory; } catch { }
        var shopState = inventory != null
            ? BuildFakeMerchantShopItems(inventory)
            : new Dictionary<string, object?>
            {
                ["items"] = new List<Dictionary<string, object?>>(),
                ["inventory"] = "not_opened"
            };

        // Proceed button (read-only)
        if (fakeMerchantNode != null)
        {
            var proceedButton = FindFirst<NProceedButton>(fakeMerchantNode);
            shopState["can_proceed"] = proceedButton?.IsEnabled ?? false;
        }
        else
        {
            shopState["can_proceed"] = false;
        }

        state["shop"] = shopState;
        return state;
    }

    private static Dictionary<string, object?> BuildFakeMerchantShopItems(MerchantInventory? inventory)
    {
        var state = new Dictionary<string, object?>();

        if (inventory == null)
        {
            state["items"] = new List<Dictionary<string, object?>>();
            state["inventory"] = "not_opened";
            return state;
        }

        var items = new List<Dictionary<string, object?>>();
        int index = 0;

        // FakeMerchant only sells relics (no cards, potions, or card removal)
        foreach (var entry in inventory.RelicEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "relic",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } relic)
            {
                item["relic_id"] = relic.Id.Entry;
                item["relic_name"] = SafeGetText(() => relic.Title);
                item["relic_description"] = SafeGetText(() => relic.DynamicDescription);
                item["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
            }
            items.Add(item);
            index++;
        }

        state["items"] = items;
        return state;
    }

    private static Dictionary<string, object?> BuildRestSiteState(RestSiteRoom restSiteRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var options = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var opt in restSiteRoom.Options)
        {
            options.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["id"] = opt.OptionId,
                ["name"] = SafeGetText(() => opt.Title),
                ["description"] = SafeGetText(() => opt.Description),
                ["is_enabled"] = opt.IsEnabled
            });
            index++;
        }
        state["options"] = options;

        var proceedButton = NRestSiteRoom.Instance?.ProceedButton;
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildShopState(MerchantRoom merchantRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Passive adaptation: upstream auto-called NMerchantRoom.OpenInventory() before
        // building this section. The inventory is populated model-side in MerchantRoom.Enter,
        // so it is readable without touching the UI. v0.107.1: MerchantRoom.Inventory was
        // replaced by per-player Inventories + GetLocalInventory() (indexes by local player
        // slot; guarded — throws before the room is fully entered).
        bool inventoryUiOpen = false;
        try { inventoryUiOpen = NMerchantRoom.Instance?.Inventory?.IsOpen ?? false; } catch { }
        state["inventory_open"] = inventoryUiOpen;

        MerchantInventory? inventory = null;
        try { inventory = merchantRoom.GetLocalInventory(); } catch { }
        if (inventory == null)
        {
            state["items"] = new List<Dictionary<string, object?>>();
            state["can_proceed"] = NMerchantRoom.Instance?.ProceedButton?.IsEnabled ?? false;
            state["error"] =
                "Shop inventory is not ready yet (null). Often happens right after entering the merchant from the map.";
            return state;
        }

        var items = new List<Dictionary<string, object?>>();
        int index = 0;

        // Cards
        foreach (var entry in inventory.CardEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "card",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold,
                ["on_sale"] = entry.IsOnSale
            };
            if (entry.CreationResult?.Card is { } card)
            {
                var cardInfo = BuildCardInfo(card);
                item["card_id"] = cardInfo["id"];
                item["card_name"] = cardInfo["name"];
                item["card_type"] = cardInfo["type"];
                item["card_cost"] = cardInfo["cost"];
                item["card_star_cost"] = cardInfo["star_cost"];
                item["card_rarity"] = cardInfo["rarity"];
                item["card_description"] = cardInfo["description"];
                item["keywords"] = cardInfo["keywords"];
            }
            items.Add(item);
            index++;
        }

        // Relics
        foreach (var entry in inventory.RelicEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "relic",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } relic)
            {
                item["relic_id"] = relic.Id.Entry;
                item["relic_name"] = SafeGetText(() => relic.Title);
                item["relic_description"] = SafeGetText(() => relic.DynamicDescription);
                item["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
            }
            items.Add(item);
            index++;
        }

        // Potions
        foreach (var entry in inventory.PotionEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "potion",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } potion)
            {
                item["potion_id"] = potion.Id.Entry;
                item["potion_name"] = SafeGetText(() => potion.Title);
                item["potion_description"] = SafeGetText(() => potion.DynamicDescription);
                item["keywords"] = BuildHoverTips(potion.ExtraHoverTips);
            }
            items.Add(item);
            index++;
        }

        // Card removal
        if (inventory.CardRemovalEntry is { } removal)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "card_removal",
                ["price"] = removal.Cost,
                ["is_stocked"] = removal.IsStocked,
                ["can_afford"] = removal.EnoughGold
            });
        }

        state["items"] = items;

        var proceedButton = NMerchantRoom.Instance?.ProceedButton;
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildTreasureState(TreasureRoom treasureRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);

        if (treasureUI == null)
        {
            state["message"] = "Treasure room loading...";
            return state;
        }

        // Passive adaptation: upstream ForceClick()ed the chest here. We only READ the
        // chest button ("%Chest", NButton — v0.99.1 field _chestButton): while it is
        // still enabled the chest is unopened and its contents remain hidden info.
        var chestButton = GetInstanceFieldValue(treasureUI, "_chestButton") as NClickableControl
                          ?? treasureUI.GetNodeOrNull<NClickableControl>("%Chest");
        bool? chestOpened = chestButton != null ? !chestButton.IsEnabled : null;
        state["chest_opened"] = chestOpened;

        if (GetInstanceFieldValue(treasureUI, "_hasRelicBeenClaimed") is bool claimed)
            state["relic_claimed"] = claimed;

        // Show relics available for picking — only once the chest has revealed them.
        var relicCollection = GetInstanceFieldValue(treasureUI, "_relicCollection") as NTreasureRoomRelicCollection
                              ?? treasureUI.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
        if (chestOpened != false && relicCollection?.Visible == true)
        {
            var holders = FindAll<NTreasureRoomRelicHolder>(relicCollection)
                .Where(h => h.IsEnabled && h.Visible)
                .ToList();

            var relics = new List<Dictionary<string, object?>>();
            int index = 0;
            foreach (var holder in holders)
            {
                var relic = holder.Relic?.Model;
                if (relic == null) continue;
                relics.Add(new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["id"] = relic.Id.Entry,
                    ["name"] = SafeGetText(() => relic.Title),
                    ["description"] = SafeGetText(() => relic.DynamicDescription),
                    ["rarity"] = relic.Rarity.ToString(),
                    ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
                });
                index++;
            }
            state["relics"] = relics;
        }
        else if (chestOpened != true)
        {
            state["relics"] = "not_opened";
        }

        state["can_proceed"] = treasureUI.ProceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildCrystalSphereState(NCrystalSphereScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var instructionsTitle = screen.GetNodeOrNull<Godot.Control>("%InstructionsTitle");
        if (instructionsTitle != null)
        {
            var textVariant = instructionsTitle.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["instructions_title"] = StripRichTextTags(textVariant.AsString());
        }

        var instructionsDescription = screen.GetNodeOrNull<Godot.Control>("%InstructionsDescription");
        if (instructionsDescription != null)
        {
            var textVariant = instructionsDescription.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["instructions_description"] = StripRichTextTags(textVariant.AsString());
        }

        var cells = FindAll<NCrystalSphereCell>(screen);
        state["grid_width"] = cells.Count > 0 ? cells.Max(c => c.Entity.X) + 1 : 0;
        state["grid_height"] = cells.Count > 0 ? cells.Max(c => c.Entity.Y) + 1 : 0;

        var cellStates = new List<Dictionary<string, object?>>();
        var clickableCells = new List<Dictionary<string, object?>>();
        foreach (var cell in cells.OrderBy(c => c.Entity.Y).ThenBy(c => c.Entity.X))
        {
            var cellState = new Dictionary<string, object?>
            {
                ["x"] = cell.Entity.X,
                ["y"] = cell.Entity.Y,
                ["is_hidden"] = cell.Entity.IsHidden,
                ["is_clickable"] = cell.Entity.IsHidden && cell.Visible,
                ["is_highlighted"] = cell.Entity.IsHighlighted,
                ["is_hovered"] = cell.Entity.IsHovered
            };

            if (!cell.Entity.IsHidden && cell.Entity.Item != null)
            {
                cellState["item_type"] = cell.Entity.Item.GetType().Name;
                cellState["is_good"] = cell.Entity.Item.IsGood;
            }

            cellStates.Add(cellState);
            if (cell.Entity.IsHidden && cell.Visible)
            {
                clickableCells.Add(new Dictionary<string, object?>
                {
                    ["x"] = cell.Entity.X,
                    ["y"] = cell.Entity.Y
                });
            }
        }
        state["cells"] = cellStates;
        state["clickable_cells"] = clickableCells;

        var revealedItems = new List<Dictionary<string, object?>>();
        foreach (var item in cells
                     .Where(c => !c.Entity.IsHidden && c.Entity.Item != null)
                     .Select(c => c.Entity.Item!)
                     .Distinct())
        {
            revealedItems.Add(new Dictionary<string, object?>
            {
                ["item_type"] = item.GetType().Name,
                ["x"] = item.Position.X,
                ["y"] = item.Position.Y,
                ["width"] = item.Size.X,
                ["height"] = item.Size.Y,
                ["is_good"] = item.IsGood
            });
        }
        state["revealed_items"] = revealedItems;

        var bigButton = screen.GetNodeOrNull<Godot.Control>("%BigDivinationButton");
        var smallButton = screen.GetNodeOrNull<Godot.Control>("%SmallDivinationButton");
        bool bigVisible = bigButton?.Visible == true;
        bool smallVisible = smallButton?.Visible == true;
        bool bigActive = bigButton?.GetNodeOrNull<Godot.Control>("%Outline")?.Visible == true;
        bool smallActive = smallButton?.GetNodeOrNull<Godot.Control>("%Outline")?.Visible == true;

        state["tool"] = bigActive ? "big" : smallActive ? "small" : "none";
        state["can_use_big_tool"] = bigVisible;
        state["can_use_small_tool"] = smallVisible;

        var divinationsLeft = screen.GetNodeOrNull<Godot.Control>("%DivinationsLeft");
        if (divinationsLeft != null)
        {
            var textVariant = divinationsLeft.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["divinations_left_text"] = StripRichTextTags(textVariant.AsString());
        }

        state["can_proceed"] = FindCrystalSphereProceedButton(screen) != null;

        return state;
    }

    private static NProceedButton? FindCrystalSphereProceedButton(NCrystalSphereScreen screen)
    {
        var namedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (IsControlVisibleOrActionable(namedButton))
            return namedButton;

        return FindAll<NProceedButton>(screen)
            .FirstOrDefault(IsControlVisibleOrActionable);
    }
}
