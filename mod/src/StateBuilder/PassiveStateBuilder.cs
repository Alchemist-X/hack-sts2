// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs.
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations: passive/read-only (all UI-mutating calls removed), JSON-only output
// (System.Text.Json.Nodes), RunState injected by the caller instead of RunManager debug getters,
// per-section error guards (never throws), verified against game v0.99.1 decompiled source.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

/// <summary>
/// Passive, side-effect-free snapshot builder for the observation channel.
/// Reads game/UI state only; never clicks, opens, or mutates anything.
/// Hidden-information hygiene: draw pile contents are sorted (not true order) and
/// no RNG seed appears in any payload (seed belongs to the run manifest; true draw
/// order arrives via the events channel, not here).
/// </summary>
public static partial class PassiveStateBuilder
{
    /// <summary>
    /// Builds a snapshot of the currently observable game state.
    /// <paramref name="runState"/> is the RunState cached from RunManager.RunStarted.
    /// Returns false only on catastrophic failure; individual screen sections that
    /// fail are embedded as {"section_error": "..."} and the snapshot still succeeds.
    /// </summary>
    public static bool TryBuildSnapshot(RunState runState, out string screen, out JsonNode state)
    {
        try
        {
            var result = BuildSnapshotDict(runState);
            screen = result.TryGetValue("state_type", out var st) ? st as string ?? "unknown" : "unknown";
            state = ToJsonNode(result) ?? new JsonObject();
            return true;
        }
        catch (Exception ex)
        {
            screen = "error";
            state = new JsonObject
            {
                ["state_type"] = "error",
                ["section_error"] = ex.GetType().Name + ": " + ex.Message
            };
            return false;
        }
    }

    private static Dictionary<string, object?> BuildSnapshotDict(RunState? runState)
    {
        var result = new Dictionary<string, object?>();
        var tree = Godot.Engine.GetMainLoop() as SceneTree;

        // Tutorial (FTUE) popups and yes/no popups can block input at any point
        // during a run; report them as the active screen when present.
        if (tree?.Root != null)
        {
            Dictionary<string, object?>? ftueState = null;
            try { ftueState = BuildVisibleFtueState(tree.Root); }
            catch (Exception ex) { result["ftue_probe_error"] = ex.GetType().Name + ": " + ex.Message; }
            if (ftueState != null)
                return ftueState;
        }

        // Sts2Recorder adaptation: upstream called RunManager.Instance.DebugOnlyGetState()
        // here; we use the RunState the caller cached from RunManager.RunStarted.
        if (runState == null)
        {
            result["state_type"] = "unknown";
            result["message"] = "No run state available.";
            return result;
        }

        // Overlays can appear on top of any room (events, rest sites, combat).
        // Rewards/card-reward overlays defer to the map - they may linger on the
        // overlay stack while the map opens after the player clicks proceed.
        IOverlayScreen? topOverlay = null;
        try { topOverlay = NOverlayStack.Instance?.Peek(); } catch { }
        var currentRoom = runState.CurrentRoom;
        bool mapIsOpen = false;
        try { mapIsOpen = IsMapScreenOpenOrVisible(); } catch { }

        if (topOverlay is NCardGridSelectionScreen cardSelectScreen)
        {
            result["state_type"] = "card_select";
            result["card_select"] = Guarded(() => BuildCardSelectState(cardSelectScreen, runState));
        }
        else if (topOverlay is NChooseACardSelectionScreen chooseCardScreen)
        {
            result["state_type"] = "card_select";
            result["card_select"] = Guarded(() => BuildChooseCardState(chooseCardScreen, runState));
        }
        else if (topOverlay is NChooseABundleSelectionScreen bundleScreen)
        {
            result["state_type"] = "bundle_select";
            result["bundle_select"] = Guarded(() => BuildBundleSelectState(bundleScreen, runState));
        }
        else if (topOverlay is NChooseARelicSelection relicSelectScreen)
        {
            result["state_type"] = "relic_select";
            result["relic_select"] = Guarded(() => BuildRelicSelectState(relicSelectScreen, runState));
        }
        else if (!mapIsOpen && topOverlay is NCrystalSphereScreen crystalSphereScreen)
        {
            // Mirror the NRewardsScreen guard below: the Crystal Sphere overlay
            // lingers on the stack after proceed is clicked (only ClearScreens
            // on the next room transition pops it). Once the map opens on top,
            // report "map" so state matches the screen the player can interact with.
            result["state_type"] = "crystal_sphere";
            result["crystal_sphere"] = Guarded(() => BuildCrystalSphereState(crystalSphereScreen, runState));
        }
        else if (!mapIsOpen && topOverlay is NCardRewardSelectionScreen cardRewardScreen)
        {
            result["state_type"] = "card_reward";
            result["card_reward"] = Guarded(() => BuildCardRewardState(cardRewardScreen));
        }
        else if (!mapIsOpen && topOverlay is NRewardsScreen rewardsScreen)
        {
            result["state_type"] = "rewards";
            result["rewards"] = Guarded(() => BuildRewardsState(rewardsScreen, runState));
        }
        else if (topOverlay is NGameOverScreen)
        {
            result["state_type"] = "game_over";
            result["game_over"] = new Dictionary<string, object?>
            {
                ["message"] = "Run ended."
            };
        }
        else if (topOverlay is IOverlayScreen
                 && topOverlay is not NRewardsScreen
                 && topOverlay is not NCardRewardSelectionScreen
                 && topOverlay is not NCrystalSphereScreen)
        {
            // Catch-all for unhandled overlays - prevents blind spots.
            // Overlays that linger on the stack while the map takes over
            // (rewards, card reward, Crystal Sphere) are excluded so the
            // fallback below reports "map" once mapIsOpen is true.
            result["state_type"] = "overlay";
            result["overlay"] = new Dictionary<string, object?>
            {
                ["screen_type"] = topOverlay.GetType().Name,
                ["message"] = $"An overlay ({topOverlay.GetType().Name}) is active."
            };
        }
        else if (mapIsOpen)
        {
            result["state_type"] = "map";
            result["map"] = Guarded(() => BuildMapState(runState));
        }
        else if (currentRoom is CombatRoom combatRoom)
        {
            bool combatInProgress = false;
            try { combatInProgress = CombatManager.Instance.IsInProgress; } catch { }
            if (combatInProgress)
            {
                // Check for in-combat hand card selection (e.g. "Select a card to exhaust")
                NPlayerHand? playerHand = null;
                try { playerHand = NPlayerHand.Instance; } catch { }
                if (playerHand != null && playerHand.IsInCardSelection)
                {
                    result["state_type"] = "hand_select";
                    result["hand_select"] = Guarded(() => BuildHandSelectState(playerHand, runState));
                    result["battle"] = Guarded(() => BuildBattleState(runState, combatRoom));
                }
                else
                {
                    result["state_type"] = combatRoom.RoomType.ToString().ToLowerInvariant(); // monster, elite, boss
                    result["battle"] = Guarded(() => BuildBattleState(runState, combatRoom));
                }
            }
            else
            {
                // After combat ends - reward/card overlays are caught by top-level checks above.
                // Only handle the brief transition before rewards appear.
                result["state_type"] = combatRoom.RoomType.ToString().ToLowerInvariant();
                result["message"] = "Combat ended. Waiting for rewards...";
            }
        }
        else if (currentRoom is EventRoom eventRoom)
        {
            bool isFakeMerchant = false;
            try { isFakeMerchant = eventRoom.CanonicalEvent is FakeMerchant; } catch { }
            if (isFakeMerchant)
            {
                result["state_type"] = "fake_merchant";
                result["fake_merchant"] = Guarded(() => BuildFakeMerchantState(eventRoom, runState));
            }
            else
            {
                result["state_type"] = "event";
                result["event"] = Guarded(() => BuildEventState(eventRoom, runState));
            }
        }
        else if (currentRoom is MapRoom)
        {
            result["state_type"] = "map";
            result["map"] = Guarded(() => BuildMapState(runState));
        }
        else if (currentRoom is MerchantRoom merchantRoom)
        {
            result["state_type"] = "shop";
            result["shop"] = Guarded(() => BuildShopState(merchantRoom, runState));
        }
        else if (currentRoom is RestSiteRoom restSiteRoom)
        {
            result["state_type"] = "rest_site";
            result["rest_site"] = Guarded(() => BuildRestSiteState(restSiteRoom, runState));
        }
        else if (currentRoom is TreasureRoom treasureRoom)
        {
            result["state_type"] = "treasure";
            result["treasure"] = Guarded(() => BuildTreasureState(treasureRoom, runState));
        }
        else
        {
            result["state_type"] = "unknown";
            result["room_type"] = currentRoom?.GetType().Name;
        }

        // Common run info (no seed here - seed lives in the run manifest only).
        result["run"] = Guarded(() => new Dictionary<string, object?>
        {
            ["act"] = runState.CurrentActIndex + 1,
            ["floor"] = runState.TotalFloor,
            ["ascension"] = runState.AscensionLevel
        });

        // Always include full player data (relics, potions, etc.) on every screen.
        try
        {
            var player = LocalContext.GetMe(runState);
            if (player != null)
                result["player"] = Guarded(() => BuildPlayerState(player));
        }
        catch (Exception ex)
        {
            result["player"] = SectionError(ex);
        }

        return result;
    }

    /// <summary>Runs a screen-section builder, converting any exception into a section_error payload.</summary>
    private static Dictionary<string, object?> Guarded(Func<Dictionary<string, object?>> build)
    {
        try { return build(); }
        catch (Exception ex) { return SectionError(ex); }
    }

    private static Dictionary<string, object?> SectionError(Exception ex)
    {
        return new Dictionary<string, object?>
        {
            ["section_error"] = ex.GetType().Name + ": " + ex.Message
        };
    }

    /// <summary>
    /// Converts the upstream Dictionary/List object graph into System.Text.Json nodes.
    /// (Upstream serialized via JsonSerializer for its HTTP responses; the recorder
    /// hands structured JsonNode payloads to its writer instead.)
    /// </summary>
    private static JsonNode? ToJsonNode(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return node;
            case string s:
                return JsonValue.Create(s);
            case bool b:
                return JsonValue.Create(b);
            case int i:
                return JsonValue.Create(i);
            case uint u:
                return JsonValue.Create(u);
            case long l:
                return JsonValue.Create(l);
            case ulong ul:
                return JsonValue.Create(ul);
            case float f:
                return JsonValue.Create(f);
            case double d:
                return JsonValue.Create(d);
            case decimal m:
                return JsonValue.Create(m);
            case Enum e:
                return JsonValue.Create(e.ToString());
            case Dictionary<string, object?> dict:
            {
                var obj = new JsonObject();
                foreach (var pair in dict)
                    obj[pair.Key] = ToJsonNode(pair.Value);
                return obj;
            }
            case IEnumerable seq:
            {
                var arr = new JsonArray();
                foreach (var item in seq)
                    arr.Add(ToJsonNode(item));
                return arr;
            }
            default:
                return JsonValue.Create(value.ToString());
        }
    }
}
