using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// Per-decision human funnels from docs/hook-map.md. Two flavors:
/// (a) annotation hooks that open a human-attribution scope around UI handlers
///     whose enqueued GameAction the ActionPipeline records (play card, end
///     turn, use/discard potion, map vote, treasure relic pick);
/// (b) post-commit funnels recorded directly with status "committed" (events,
///     rest site, shop, rewards, player-choice overlays, chest open).
/// Every patch is applied individually; failures degrade that hook only.
/// </summary>
public static class HumanFunnels
{
    private static TrajectorySession? _restSiteSession;
    private static Action<RestSiteOption, bool, ulong>? _restSiteHandler;
    private static string? _lastChoiceContextModel;
    private static AccessTools.FieldRef<MoveToMapCoordAction, MegaCrit.Sts2.Core.Map.MapCoord>? _moveDestination;

    internal static void ApplyPatches(Harmony harmony)
    {
        _moveDestination = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<MoveToMapCoordAction, MegaCrit.Sts2.Core.Map.MapCoord>("_destination"));

        // --- (a) annotation hooks: human scope around action-enqueuing UI handlers ---
        RecorderMod.TryPatch(
            harmony,
            "patch:CardModel.TryManualPlay",
            AccessTools.Method(typeof(CardModel), nameof(CardModel.TryManualPlay)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(TryManualPlayPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NEndTurnButton.CallReleaseLogic",
            AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(CallReleaseLogicPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NEndTurnButton.SecretEndTurnLogicViaFtue",
            AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.SecretEndTurnLogicViaFtue)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SecretEndTurnPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:PotionModel.EnqueueManualUse",
            AccessTools.Method(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(EnqueueManualUsePrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NPotionPopup.OnDiscardButtonPressed",
            AccessTools.Method(typeof(NPotionPopup), "OnDiscardButtonPressed"),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(DiscardPotionPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NMapScreen.OnMapPointSelectedLocally",
            AccessTools.Method(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(MapPointSelectedPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:TreasureRoomRelicSynchronizer.PickRelicLocally",
            AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(PickRelicLocallyPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));

        // --- (b) post-commit funnels recorded with status "committed" ---
        RecorderMod.TryPatch(
            harmony,
            "patch:MoveToMapCoordAction.ExecuteAction",
            AccessTools.Method(typeof(MoveToMapCoordAction), "ExecuteAction"),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(MoveToMapCoordExecutedPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NEventRoom.OptionButtonClicked",
            AccessTools.Method(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(EventOptionClickedPrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:EventSynchronizer.SaveEventOptionToHistory",
            AccessTools.Method(typeof(EventSynchronizer), "SaveEventOptionToHistory"),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(SaveEventOptionPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:MerchantEntry.OnTryPurchaseWrapper",
            AccessTools.Method(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(MerchantPurchasePostfix)));
        // Method HIDING (not override): the card-removal overload must be patched separately.
        RecorderMod.TryPatch(
            harmony,
            "patch:MerchantCardRemovalEntry.OnTryPurchaseWrapper",
            AccessTools.Method(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(CardRemovalPurchasePostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:Reward.OnSelectWrapper",
            AccessTools.Method(typeof(Reward), nameof(Reward.OnSelectWrapper)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(RewardSelectPostfix)));
        // Reward.OnSkipped is virtual and overridden — patching the base does NOT
        // intercept overrides; each override is patched individually.
        PatchOnSkipped(harmony, typeof(CardReward));
        PatchOnSkipped(harmony, typeof(PotionReward));
        PatchOnSkipped(harmony, typeof(SpecialCardReward));
        PatchOnSkipped(harmony, typeof(RelicReward));
        PatchOnSkipped(harmony, typeof(LinkedRewardSet));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalObtainedCard), nameof(SyncObtainedCardPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalSkippedCard), nameof(SyncSkippedCardPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalObtainedRelic), nameof(SyncObtainedRelicPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalSkippedRelic), nameof(SyncSkippedRelicPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalObtainedPotion), nameof(SyncObtainedPotionPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalSkippedPotion), nameof(SyncSkippedPotionPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalObtainedGold), nameof(SyncObtainedGoldPrefix));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalGoldLost), nameof(SyncGoldLostPrefix));
        RecorderMod.TryPatch(
            harmony,
            "patch:PlayerChoiceSynchronizer.SyncLocalChoice",
            AccessTools.Method(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SyncLocalChoicePrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:PlayerChoiceContext.PushModel",
            AccessTools.Method(typeof(PlayerChoiceContext), nameof(PlayerChoiceContext.PushModel)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(PushModelPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NTreasureRoom.OnChestButtonReleased",
            AccessTools.Method(typeof(NTreasureRoom), "OnChestButtonReleased"),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(ChestButtonPrefix)));
    }

    private static void PatchOnSkipped(Harmony harmony, Type rewardType)
    {
        RecorderMod.TryPatch(
            harmony,
            $"patch:{rewardType.Name}.OnSkipped",
            AccessTools.DeclaredMethod(rewardType, "OnSkipped"),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(RewardSkippedPostfix)));
    }

    private static void PatchSyncLocal(Harmony harmony, string methodName, string patchName)
    {
        RecorderMod.TryPatch(
            harmony,
            $"patch:RewardSynchronizer.{methodName}",
            AccessTools.Method(typeof(RewardSynchronizer), methodName),
            prefix: new HarmonyMethod(typeof(HumanFunnels), patchName));
    }

    /// <summary>RestSiteSynchronizer is recreated each run — resubscribe on every RunStarted.</summary>
    internal static void OnRunStarted(TrajectorySession session)
    {
        _lastChoiceContextModel = null;
        try
        {
            var synchronizer = RunManager.Instance.RestSiteSynchronizer;
            _restSiteSession = session;
            _restSiteHandler = OnRestSiteOptionChosen;
            synchronizer.AfterPlayerOptionChosen += _restSiteHandler;
        }
        catch (Exception ex)
        {
            session.AddDegradedHook("event:RestSiteSynchronizer.AfterPlayerOptionChosen");
            RecorderMod.Report("HumanFunnels.OnRunStarted", ex);
        }
    }

    internal static void OnRunEnding()
    {
        try
        {
            if (_restSiteHandler != null)
            {
                RunManager.Instance.RestSiteSynchronizer.AfterPlayerOptionChosen -= _restSiteHandler;
            }
        }
        catch
        {
            // The synchronizer may already be disposed during CleanUp.
        }
        _restSiteHandler = null;
        _restSiteSession = null;
        _lastChoiceContextModel = null;
    }

    private static void RecordCommitted(string source, string kind, JsonObject parameters)
    {
        try
        {
            RecorderMod.Session?.RecordAction(source, kind, parameters, "committed", null);
            Snapshots.RequestDeferredSnapshot();
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"HumanFunnels.RecordCommitted:{kind}", ex);
        }
    }

    /// <summary>Merchant purchases fire SyncLocalObtained* too — dedupe by room context.</summary>
    private static bool IsShopContext()
    {
        try
        {
            var room = RecorderMod.CurrentRunState?.CurrentRoom;
            if (room is MerchantRoom)
            {
                return true;
            }
            return room is EventRoom eventRoom && eventRoom.CanonicalEvent is FakeMerchant;
        }
        catch
        {
            return false;
        }
    }

    // --- annotation prefixes / shared finalizer ---

    private static void TryManualPlayPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:CardModel.TryManualPlay");
    }

    private static void CallReleaseLogicPrefix()
    {
        // The enqueued type disambiguates: IsPlayerReadyToEndTurn false -> EndPlayerTurnAction,
        // true -> UndoEndPlayerTurnAction (the game reads it inside CallReleaseLogic itself).
        ActionPipeline.BeginHumanScope("hook:NEndTurnButton.CallReleaseLogic");
    }

    private static void SecretEndTurnPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:NEndTurnButton.SecretEndTurnLogicViaFtue");
    }

    private static void EnqueueManualUsePrefix(PotionModel __instance)
    {
        try
        {
            // Local machine only: remote uses arrive via net messages, not this method.
            if (LocalContext.IsMe(__instance.Owner))
            {
                ActionPipeline.BeginHumanScope("hook:PotionModel.EnqueueManualUse");
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.EnqueueManualUsePrefix", ex);
        }
    }

    private static void DiscardPotionPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:NPotionPopup.OnDiscardButtonPressed");
    }

    private static void MapPointSelectedPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:NMapScreen.OnMapPointSelectedLocally");
    }

    private static void PickRelicLocallyPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:TreasureRoomRelicSynchronizer.PickRelicLocally");
    }

    private static void EndScopeFinalizer()
    {
        ActionPipeline.EndHumanScope();
    }

    // --- committed funnels ---

    /// <summary>Committed map destination (runs at commit, before the travel animation).</summary>
    private static void MoveToMapCoordExecutedPostfix(MoveToMapCoordAction __instance)
    {
        try
        {
            if (RecorderMod.Session == null)
            {
                return;
            }
            var parameters = new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(() => (long)__instance.OwnerId),
            };
            if (_moveDestination != null)
            {
                parameters["destination"] = JsonDescribe.Try(() =>
                {
                    var coord = _moveDestination(__instance);
                    return (JsonNode?)new JsonObject { ["col"] = coord.col, ["row"] = coord.row };
                });
            }
            RecordCommitted("hook:MoveToMapCoordAction.ExecuteAction", "travel_to_map_coord", parameters);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.MoveToMapCoordExecutedPostfix", ex);
        }
    }

    /// <summary>
    /// IsProceed options run option.Chosen() directly and bypass the synchronizer —
    /// this prefix is the only capture for proceed clicks (and for options with
    /// ShouldSaveChoiceToHistory == false the game itself never records).
    /// </summary>
    private static void EventOptionClickedPrefix(EventOption option, int index)
    {
        try
        {
            if (RecorderMod.Session == null || option == null || option.IsLocked)
            {
                return;
            }
            if (!option.IsProceed)
            {
                return; // Real choices are captured post-commit via SaveEventOptionToHistory.
            }
            RecordCommitted("hook:NEventRoom.OptionButtonClicked", "event_proceed", new JsonObject
            {
                ["index"] = index,
                ["text_key"] = JsonDescribe.Try(() => option.TextKey),
                ["title"] = JsonDescribe.Try(() => option.Title?.GetRawText()),
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.EventOptionClickedPrefix", ex);
        }
    }

    /// <summary>Fires in singleplayer AND for remote players in co-op — filter on the player arg.</summary>
    private static void SaveEventOptionPostfix(Player player, EventOption option)
    {
        try
        {
            if (RecorderMod.Session == null || !LocalContext.IsMe(player))
            {
                return;
            }
            var variables = new JsonObject();
            try
            {
                foreach (var pair in option.HistoryName.Variables)
                {
                    variables[pair.Key] = pair.Value?.ToString();
                }
            }
            catch
            {
                // Variables are cosmetic; drop them on failure.
            }
            RecordCommitted("hook:EventSynchronizer.SaveEventOptionToHistory", "event_option", new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(() => (long)player.NetId),
                ["title_key"] = JsonDescribe.Try(() => option.HistoryName?.LocEntryKey),
                ["title"] = JsonDescribe.Try(() => option.HistoryName?.GetRawText()),
                ["variables"] = variables,
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.SaveEventOptionPostfix", ex);
        }
    }

    private static void OnRestSiteOptionChosen(RestSiteOption option, bool success, ulong playerId)
    {
        try
        {
            if (_restSiteSession == null || RecorderMod.Session != _restSiteSession)
            {
                return;
            }
            if (playerId != LocalContext.NetId || !success)
            {
                return; // success=false means the sub-flow was backed out (no commitment).
            }
            RecordCommitted("event:RestSiteSynchronizer.AfterPlayerOptionChosen", "rest_site_option", new JsonObject
            {
                ["option_id"] = JsonDescribe.Try(() => option.OptionId),
                ["player"] = (long)playerId,
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.OnRestSiteOptionChosen", ex);
        }
    }

    /// <summary>
    /// Async wrapper: the postfix runs at first await — item identity and cost are
    /// captured synchronously, and the record is committed only when the returned
    /// Task&lt;bool&gt; resolves true (false = failed/cancelled purchase).
    /// </summary>
    private static void MerchantPurchasePostfix(MerchantEntry __instance, Task<bool> __result, bool ignoreCost)
    {
        RecordPurchaseWhenComplete(__instance, __result, ignoreCost, "shop_purchase",
            "hook:MerchantEntry.OnTryPurchaseWrapper");
    }

    private static void CardRemovalPurchasePostfix(MerchantCardRemovalEntry __instance, Task<bool> __result, bool ignoreCost)
    {
        RecordPurchaseWhenComplete(__instance, __result, ignoreCost, "shop_card_removal",
            "hook:MerchantCardRemovalEntry.OnTryPurchaseWrapper");
    }

    private static void RecordPurchaseWhenComplete(
        MerchantEntry entry, Task<bool>? result, bool ignoreCost, string kind, string source)
    {
        try
        {
            if (RecorderMod.Session == null || result == null)
            {
                return;
            }
            var parameters = DescribeMerchantEntry(entry);
            parameters["ignore_cost"] = ignoreCost; // true = relic-granted free purchase, not human gold spend
            result.ContinueWith(task =>
            {
                try
                {
                    if (task.Status != TaskStatus.RanToCompletion || !task.Result)
                    {
                        return;
                    }
                    RecorderMod.RunOnMainThread(() => RecordCommitted(source, kind, parameters));
                }
                catch (Exception ex)
                {
                    RecorderMod.Report("HumanFunnels.RecordPurchaseWhenComplete", ex);
                }
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.RecordPurchaseWhenComplete", ex);
        }
    }

    private static JsonObject DescribeMerchantEntry(MerchantEntry entry)
    {
        var parameters = new JsonObject
        {
            ["cost"] = JsonDescribe.Try<int?>(() => entry.Cost),
        };
        switch (entry)
        {
            case MerchantCardEntry cardEntry:
                parameters["item"] = "card";
                parameters["item_id"] = JsonDescribe.Try(() => cardEntry.CreationResult?.Card.Id.ToString());
                parameters["on_sale"] = JsonDescribe.Try<bool?>(() => cardEntry.IsOnSale);
                break;
            case MerchantRelicEntry relicEntry:
                parameters["item"] = "relic";
                parameters["item_id"] = JsonDescribe.Try(() => relicEntry.Model?.Id.ToString());
                break;
            case MerchantPotionEntry potionEntry:
                parameters["item"] = "potion";
                parameters["item_id"] = JsonDescribe.Try(() => potionEntry.Model?.Id.ToString());
                break;
            case MerchantCardRemovalEntry:
                parameters["item"] = "card_removal";
                break;
            default:
                parameters["item"] = JsonDescribe.SnakeCase(entry.GetType().Name);
                break;
        }
        return parameters;
    }

    /// <summary>Non-virtual wrapper — one patch covers all Reward subclasses.</summary>
    private static void RewardSelectPostfix(Reward __instance, Task<bool> __result)
    {
        try
        {
            if (RecorderMod.Session == null || __result == null)
            {
                return;
            }
            __result.ContinueWith(task =>
            {
                try
                {
                    if (task.Status != TaskStatus.RanToCompletion || !task.Result)
                    {
                        return; // false = flow backed out (full belt, closed card screen).
                    }
                    // Claimed-item fields are only set after the task completes;
                    // serialize on the main thread via the pump.
                    RecorderMod.RunOnMainThread(() => RecordCommitted(
                        "hook:Reward.OnSelectWrapper", "reward_taken", DescribeReward(__instance)));
                }
                catch (Exception ex)
                {
                    RecorderMod.Report("HumanFunnels.RewardSelectPostfix", ex);
                }
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.RewardSelectPostfix", ex);
        }
    }

    private static void RewardSkippedPostfix(Reward __instance)
    {
        try
        {
            if (RecorderMod.Session == null)
            {
                return;
            }
            RecordCommitted("hook:" + __instance.GetType().Name + ".OnSkipped", "reward_skipped",
                DescribeReward(__instance));
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.RewardSkippedPostfix", ex);
        }
    }

    private static JsonObject DescribeReward(Reward reward)
    {
        var parameters = new JsonObject
        {
            ["reward_type"] = JsonDescribe.SnakeCase(reward.GetType().Name),
            ["set_index"] = JsonDescribe.Try<int?>(() => reward.RewardsSetIndex),
            ["player"] = JsonDescribe.Try<long?>(() => (long)reward.Player.NetId),
        };
        switch (reward)
        {
            case PotionReward potionReward:
                parameters["item_id"] = JsonDescribe.Try(
                    () => (potionReward.ClaimedPotion ?? potionReward.Potion)?.Id.ToString());
                break;
            case RelicReward relicReward:
                parameters["item_id"] = JsonDescribe.Try(() => relicReward.ClaimedRelic?.Id.ToString());
                break;
            case GoldReward goldReward:
                parameters["amount"] = JsonDescribe.Try<int?>(() => goldReward.Amount);
                break;
        }
        return parameters;
    }

    // --- RewardSynchronizer.SyncLocal* prefixes (final committed acquisitions) ---

    private static void SyncObtainedCardPrefix(CardModel card) =>
        RecordSyncLocal("card_reward_obtained", new JsonObject { ["card_id"] = JsonDescribe.Model(card) });

    private static void SyncSkippedCardPrefix(CardModel card) =>
        RecordSyncLocal("card_reward_skipped", new JsonObject { ["card_id"] = JsonDescribe.Model(card) });

    private static void SyncObtainedRelicPrefix(RelicModel relic) =>
        RecordSyncLocal("relic_reward_obtained", new JsonObject { ["relic_id"] = JsonDescribe.Model(relic) });

    private static void SyncSkippedRelicPrefix(RelicModel relic) =>
        RecordSyncLocal("relic_reward_skipped", new JsonObject { ["relic_id"] = JsonDescribe.Model(relic) });

    private static void SyncObtainedPotionPrefix(PotionModel potion) =>
        RecordSyncLocal("potion_reward_obtained", new JsonObject { ["potion_id"] = JsonDescribe.Model(potion) });

    private static void SyncSkippedPotionPrefix(PotionModel potion) =>
        RecordSyncLocal("potion_reward_skipped", new JsonObject { ["potion_id"] = JsonDescribe.Model(potion) });

    private static void SyncObtainedGoldPrefix(int goldAmount) =>
        RecordSyncLocal("gold_obtained", new JsonObject { ["amount"] = goldAmount });

    private static void SyncGoldLostPrefix(int goldLost) =>
        RecordSyncLocal("gold_lost", new JsonObject { ["amount"] = goldLost });

    private static void RecordSyncLocal(string kind, JsonObject parameters)
    {
        try
        {
            if (RecorderMod.Session == null || IsShopContext())
            {
                return; // Shop acquisitions are recorded by the merchant purchase hook.
            }
            RecordCommitted("hook:RewardSynchronizer.SyncLocal", kind, parameters);
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"HumanFunnels.RecordSyncLocal:{kind}", ex);
        }
    }

    // --- player-choice overlays (the single most valuable decision hook) ---

    private static void SyncLocalChoicePrefix(Player player, uint choiceId, PlayerChoiceResult result)
    {
        try
        {
            if (RecorderMod.Session == null || !LocalContext.IsMe(player))
            {
                return;
            }
            var parameters = new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(() => (long)player.NetId),
                ["choice_id"] = (long)choiceId,
                ["choice_type"] = JsonDescribe.Try(() => result.ChoiceType.ToString()),
                ["context_model"] = _lastChoiceContextModel,
            };
            var picked = DecodeChoice(result, parameters);
            parameters["picked"] = picked;
            RecordCommitted("hook:PlayerChoiceSynchronizer.SyncLocalChoice", "player_choice", parameters);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.SyncLocalChoicePrefix", ex);
        }
    }

    /// <summary>
    /// Skippable flows still call SyncLocalChoice with an empty/-1 result — the
    /// decoded payload distinguishes pick vs skip.
    /// </summary>
    private static bool DecodeChoice(PlayerChoiceResult result, JsonObject parameters)
    {
        try
        {
            switch (result.ChoiceType)
            {
                case PlayerChoiceType.Index:
                {
                    var indexes = JsonDescribe.Try<List<int>>(() => result.AsIndexes()) ?? new List<int>();
                    var array = new JsonArray();
                    var picked = false;
                    foreach (var index in indexes)
                    {
                        array.Add(index);
                        picked |= index >= 0;
                    }
                    parameters["indexes"] = array;
                    parameters["index"] = JsonDescribe.Try<int?>(() => result.AsIndex());
                    return picked;
                }
                case PlayerChoiceType.Player:
                {
                    var playerId = JsonDescribe.Try<ulong?>(() => result.AsPlayerId());
                    parameters["chosen_player"] = playerId.HasValue ? (long?)(long)playerId.Value : null;
                    return playerId.HasValue;
                }
                case PlayerChoiceType.CanonicalCard:
                case PlayerChoiceType.CombatCard:
                case PlayerChoiceType.DeckCard:
                case PlayerChoiceType.MutableCard:
                {
                    var cards = new JsonArray();
                    var count = 0;
                    var models = JsonDescribe.Try<IEnumerable<CardModel>>(() => result.AsCards(result.ChoiceType));
                    if (models != null)
                    {
                        foreach (var card in models)
                        {
                            cards.Add(JsonDescribe.Model(card));
                            count++;
                        }
                    }
                    parameters["cards"] = cards;
                    return count > 0;
                }
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.DecodeChoice", ex);
            return false;
        }
    }

    /// <summary>Remembers which model asked for the current player choice.</summary>
    private static void PushModelPostfix(AbstractModel model)
    {
        try
        {
            _lastChoiceContextModel = JsonDescribe.Model(model);
        }
        catch
        {
            // Annotation only; never propagate.
        }
    }

    private static void ChestButtonPrefix()
    {
        try
        {
            if (RecorderMod.Session == null)
            {
                return;
            }
            RecordCommitted("hook:NTreasureRoom.OnChestButtonReleased", "open_chest", new JsonObject());
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.ChestButtonPrefix", ex);
        }
    }
}
