// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs (reward/selection sections).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations (game v0.99.1): read-only; button states are reported, never clicked.
// Addition (docs/hook-map.md, v0.111.0 addendum, R4): reward offers in the rewards state
// (card reward cards, relic id, special card), populated model-side before display.

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static Dictionary<string, object?> BuildRewardsState(NRewardsScreen rewardsScreen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Reward items
        var rewardButtons = FindAll<NRewardButton>(rewardsScreen);
        var items = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var button in rewardButtons)
        {
            if (button.Reward == null || !button.IsEnabled) continue;
            var reward = button.Reward;

            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["type"] = GetRewardTypeName(reward),
                ["description"] = SafeGetText(() => reward.Description)
            };

            // Type-specific details
            if (reward is GoldReward goldReward)
                item["gold_amount"] = goldReward.Amount;
            else if (reward is PotionReward potionReward && potionReward.Potion != null)
            {
                item["potion_id"] = potionReward.Potion.Id.Entry;
                item["potion_name"] = SafeGetText(() => potionReward.Potion.Title);
                item["potion_description"] = SafeGetText(() => potionReward.Potion.DynamicDescription);
            }
            else if (reward is RelicReward relicReward && relicReward.Relic != null)
            {
                item["relic_id"] = relicReward.Relic.Id.Entry;
                item["relic_name"] = SafeGetText(() => relicReward.Relic.Title);
                item["relic_description"] = SafeGetText(() => relicReward.Relic.DynamicDescription);
            }
            else if (reward is CardReward cardReward)
            {
                try
                {
                    var offered = new List<Dictionary<string, object?>>();
                    foreach (var card in cardReward.Cards)
                        offered.Add(BuildOfferedCard(card, offered.Count));
                    item["cards"] = offered;
                    item["can_reroll"] = cardReward.CanReroll;
                    item["can_skip"] = cardReward.CanSkip;
                }
                catch (Exception ex)
                {
                    item["cards"] = SectionError(ex);
                }
            }
            else if (reward is SpecialCardReward specialCardReward)
            {
                var card = RewardOffers.SpecialCard(specialCardReward);
                if (card != null)
                {
                    try { item["card"] = BuildOfferedCard(card, 0); }
                    catch (Exception ex) { item["card"] = SectionError(ex); }
                }
            }

            items.Add(item);
            index++;
        }
        state["items"] = items;

        // Proceed button (read-only)
        var proceedButton = FindFirst<NProceedButton>(rewardsScreen);
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    /// <summary>Display fields plus {upgrade_level, enchantment} for an offered card.</summary>
    private static Dictionary<string, object?> BuildOfferedCard(CardModel card, int index)
    {
        var cardInfo = BuildCardInfo(card);
        cardInfo["index"] = index;
        cardInfo["upgrade_level"] = card.CurrentUpgradeLevel;
        cardInfo["enchantment"] = BuildEnchantmentState(card);
        return cardInfo;
    }

    private static string GetRewardTypeName(Reward reward) => reward switch
    {
        GoldReward => "gold",
        PotionReward => "potion",
        RelicReward => "relic",
        CardReward => "card",
        SpecialCardReward => "special_card",
        CardRemovalReward => "card_removal",
        _ => reward.GetType().Name.ToLowerInvariant()
    };

    private static Dictionary<string, object?> BuildCardRewardState(NCardRewardSelectionScreen cardScreen)
    {
        var state = new Dictionary<string, object?>();

        // The screen's offered models are authoritative. UI child order changes
        // on focus, and QueueFree leaves old holders alive during a reroll frame.
        var offered = RequireSelectionField<IReadOnlyList<CardCreationResult>>(cardScreen, "_options");
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var offer in offered)
        {
            var cardInfo = BuildOfferedCard(offer.Card, index);
            cardInfo["choice_index"] = index;
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;

        var alternatives = RequireSelectionField<IReadOnlyList<CardRewardAlternative>>(cardScreen, "_extraOptions");
        state["alternatives"] = alternatives.Select((option, i) => new Dictionary<string, object?>
        {
            ["index"] = i,
            ["choice_index"] = offered.Count + i,
            ["id"] = option.OptionId,
            ["title"] = SafeGetText(() => option.Title),
            ["after_selected"] = option.AfterSelected.ToString(),
        }).ToList();
        state["can_skip"] = alternatives.Any(a => string.Equals(a.OptionId, "Skip", StringComparison.OrdinalIgnoreCase));
        state["candidate_source"] = "screen_offered_models";

        return state;
    }

    private static Dictionary<string, object?> BuildCardSelectState(NCardGridSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Screen type
        state["screen_type"] = screen switch
        {
            NDeckTransformSelectScreen => "transform",
            NDeckUpgradeSelectScreen => "upgrade",
            NDeckCardSelectScreen => "select",
            NSimpleCardSelectScreen => "simple_select",
            _ => screen.GetType().Name
        };

        // Prompt text from UI label
        var bottomLabel = screen.GetNodeOrNull("%BottomLabel");
        if (bottomLabel != null)
        {
            var textVariant = bottomLabel.Get("text");
            string? prompt = textVariant.VariantType != Godot.Variant.Type.Nil ? StripRichTextTags(textVariant.AsString()) : null;
            state["prompt"] = prompt;
        }

        // Keep original choice indices (the game's synchronized index space),
        // independent of UI sorting, scrolling and preview holders.
        // Combat-pile screens initialize their inherited _cards to EMPTY and
        // refresh the grid as the pile changes. The grid owns the full, already
        // publicly sorted set; CurrentlyDisplayedCards contains only a viewport
        // chunk. Never read the underlying draw pile's private order here.
        var offered = screen is NCombatPileCardSelectScreen
            ? RequireSelectionField<IReadOnlyList<CardModel>>(
                RequireSelectionField<NCardGrid>(screen, "_grid"), "_cards")
            : RequireSelectionField<IReadOnlyList<CardModel>>(screen, "_cards");
        var selected = GetInstanceFieldValue(screen, "_selectedCards") as IEnumerable<CardModel>;
        var selectedSet = selected?.ToHashSet() ?? new HashSet<CardModel>();
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var card in offered)
        {
            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cardInfo["choice_index"] = index;
            cardInfo["selected"] = selectedSet.Contains(card);
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;
        state["candidate_source"] = "screen_offered_models";
        AddSelectionPrefs(state, screen);

        // Preview container showing? (selection complete, awaiting confirm)
        // Upgrade screens use UpgradeSinglePreviewContainer / UpgradeMultiPreviewContainer
        var previewSingle = screen.GetNodeOrNull<Godot.Control>("%UpgradeSinglePreviewContainer");
        var previewMulti = screen.GetNodeOrNull<Godot.Control>("%UpgradeMultiPreviewContainer");
        var previewGeneric = screen.GetNodeOrNull<Godot.Control>("%PreviewContainer");
        bool previewShowing = (previewSingle?.Visible ?? false)
                            || (previewMulti?.Visible ?? false)
                            || (previewGeneric?.Visible ?? false);
        state["preview_showing"] = previewShowing;
        if (previewShowing)
        {
            var previewCards = new List<Dictionary<string, object?>>();
            AddPreviewCardsFromContainer(previewSingle, previewCards);
            AddPreviewCardsFromContainer(previewMulti, previewCards);
            AddPreviewCardsFromContainer(previewGeneric, previewCards);
            state["preview_cards"] = previewCards;
        }

        // Button states - when a preview is open, cancel goes through the
        // preview container's Cancel / PreviewCancel button (same path as
        // the action handler), not the top-level %Close button.
        bool canCancel = false;
        if (previewShowing)
        {
            foreach (var container in new[] { previewSingle, previewMulti, previewGeneric })
            {
                if (container?.Visible == true)
                {
                    var cancelBtn = container.GetNodeOrNull<NBackButton>("Cancel")
                                    ?? container.GetNodeOrNull<NBackButton>("%PreviewCancel");
                    if (cancelBtn?.IsEnabled == true) { canCancel = true; break; }
                }
            }
        }
        if (!canCancel)
        {
            var closeButton = screen.GetNodeOrNull<NBackButton>("%Close");
            canCancel = closeButton?.IsEnabled ?? false;
        }
        state["can_cancel"] = canCancel;

        // Confirm button - search all preview containers and main screen
        bool canConfirm = false;
        foreach (var container in new[] { previewSingle, previewMulti, previewGeneric })
        {
            if (container?.Visible == true)
            {
                var confirm = container.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? container.GetNodeOrNull<NConfirmButton>("%PreviewConfirm");
                if (confirm?.IsEnabled == true) { canConfirm = true; break; }
            }
        }
        if (!canConfirm)
        {
            var mainConfirm = screen.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? screen.GetNodeOrNull<NConfirmButton>("%Confirm");
            if (mainConfirm?.IsEnabled == true) canConfirm = true;
        }
        // Fallback: search entire screen tree for any enabled confirm button
        // (covers subclasses like NDeckEnchantSelectScreen)
        if (!canConfirm)
        {
            canConfirm = FindAll<NConfirmButton>(screen).Any(b => b.IsEnabled && b.IsVisibleInTree());
        }
        state["can_confirm"] = canConfirm;

        return state;
    }

    private static Dictionary<string, object?> BuildChooseCardState(NChooseACardSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "choose";

        state["prompt"] = "Choose a card.";

        var offered = RequireSelectionField<IReadOnlyList<CardModel>>(screen, "_cards");
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var card in offered)
        {
            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cardInfo["choice_index"] = index;
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;
        state["candidate_source"] = "screen_offered_models";

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        state["can_skip"] = skipButton?.IsEnabled == true && skipButton.Visible;
        state["preview_showing"] = false;
        state["can_confirm"] = false;
        state["can_cancel"] = state["can_skip"];

        return state;
    }

    private static T RequireSelectionField<T>(object screen, string field) =>
        GetInstanceFieldValue(screen, field) is T value ? value
            : throw new InvalidOperationException($"Missing offered-candidate field {screen.GetType().Name}.{field}");

    private static void AddSelectionPrefs(Dictionary<string, object?> state, object screen)
    {
        if (GetInstanceFieldValue(screen, "_prefs") is CardSelectorPrefs prefs)
        {
            state["min_select"] = prefs.MinSelect;
            state["max_select"] = prefs.MaxSelect;
            state["require_manual_confirmation"] = prefs.RequireManualConfirmation;
            state["cancelable"] = prefs.Cancelable;
        }
    }

    private static Dictionary<string, object?> BuildBundleSelectState(NChooseABundleSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "bundle";

        state["prompt"] = "Choose a bundle.";

        var bundles = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var bundle in RequireSelectionField<IReadOnlyList<IReadOnlyList<CardModel>>>(screen, "_bundles"))
        {
            var cards = new List<Dictionary<string, object?>>();
            int cardIndex = 0;
            foreach (var card in bundle)
            {
                var cardInfo = BuildCardInfo(card);
                cardInfo["index"] = cardIndex;
                cards.Add(cardInfo);
                cardIndex++;
            }

            bundles.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["choice_index"] = index,
                ["card_count"] = cards.Count,
                ["cards"] = cards
            });
            index++;
        }
        state["bundles"] = bundles;

        var previewContainer = screen.GetNodeOrNull<Godot.Control>("%BundlePreviewContainer");
        bool previewShowing = previewContainer?.Visible == true;
        state["preview_showing"] = previewShowing;

        var previewCards = new List<Dictionary<string, object?>>();
        var previewCardsContainer = screen.GetNodeOrNull<Godot.Control>("%Cards");
        if (previewCardsContainer != null)
        {
            int previewIndex = 0;
            foreach (var holder in FindAll<NPreviewCardHolder>(previewCardsContainer))
            {
                var card = holder.CardModel;
                if (card == null) continue;

                var cardInfo = BuildCardInfo(card);
                cardInfo["index"] = previewIndex;
                previewCards.Add(cardInfo);
                previewIndex++;
            }
        }
        state["preview_cards"] = previewCards;

        var cancelButton = screen.GetNodeOrNull<NBackButton>("%Cancel");
        var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        state["can_cancel"] = cancelButton?.IsEnabled == true;
        state["can_confirm"] = confirmButton?.IsEnabled == true;

        return state;
    }

    private static Dictionary<string, object?> BuildRelicSelectState(NChooseARelicSelection screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        state["prompt"] = "Choose a relic.";

        var offered = RequireSelectionField<IReadOnlyList<RelicModel>>(screen, "_relics");
        var relics = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var relic in offered)
        {
            relics.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["choice_index"] = index,
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["rarity"] = relic.Rarity.ToString(),
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            });
            index++;
        }
        state["relics"] = relics;

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        state["can_skip"] = skipButton?.IsEnabled == true && skipButton.Visible;

        return state;
    }
}
