using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
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
    private static Action<RestSiteOption, ulong>? _restSiteBeforeHandler;
    private static ConditionalWeakTable<RestSiteOption, DecisionCapture> _restDecisions = new();
    /// <summary>
    /// R5 + decision capture: the pre-click observation and clicked index of a local
    /// event-button click, consumed by the SaveEventOptionToHistory record.
    /// </summary>
    private sealed record EventInput(DecisionCapture Capture, int Index);
    private static ConditionalWeakTable<EventOption, EventInput> _eventDecisions = new();
    private static string? _lastChoiceContextModel;

    /// <summary>
    /// Rewards whose claim was initiated by the LOCAL player's explicit UI selection
    /// (RewardsSetSynchronizer.SelectLocalReward). Reward.SelectUnsynchronized fires for
    /// remote claims and programmatic auto-claims too (Draft run-start, v0.107.1) — this
    /// table is how the committed-claim record gets its human/programmatic attribution.
    /// </summary>
    private static ConditionalWeakTable<Reward, DecisionCapture> _locallySelectedRewards = new();
    private sealed record PurchaseInput(DecisionCapture Capture, JsonObject Parameters);

    /// <summary>
    /// R4: every card a local CardReward has offered since its selection began
    /// (the initial offer plus each reroll), so reward_taken can name the cards
    /// that ended up in the deck.
    /// </summary>
    private static ConditionalWeakTable<CardReward, List<CardModel>> _cardRewardOffers = new();

    internal static void ApplyPatches(Harmony harmony)
    {
        RewardOffers.Initialize();

        // --- (a) annotation hooks: human scope around action-enqueuing UI handlers ---
        RecorderMod.TryPatch(
            harmony,
            "patch:CardModel.TryManualPlay",
            () => AccessTools.Method(typeof(CardModel), nameof(CardModel.TryManualPlay)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(TryManualPlayPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NEndTurnButton.CallReleaseLogic",
            () => AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(CallReleaseLogicPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NEndTurnButton.SecretEndTurnLogicViaFtue",
            () => AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.SecretEndTurnLogicViaFtue)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SecretEndTurnPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:PotionModel.EnqueueManualUse",
            () => AccessTools.Method(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(EnqueueManualUsePrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NPotionPopup.OnDiscardButtonPressed",
            () => AccessTools.Method(typeof(NPotionPopup), "OnDiscardButtonPressed"),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(DiscardPotionPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NMapScreen.OnMapPointSelectedLocally",
            () => AccessTools.Method(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(MapPointSelectedPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));
        // v0.107.1: PickRelicLocally(int?) also covers the NEW explicit treasure-relic
        // skip button — SkipRelicLocally() is sugar that calls PickRelicLocally(null),
        // so this one scope tags both picks and skips as human.
        RecorderMod.TryPatch(
            harmony,
            "patch:TreasureRoomRelicSynchronizer.PickRelicLocally",
            () => AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(PickRelicLocallyPrefix)),
            finalizer: new HarmonyMethod(typeof(HumanFunnels), nameof(EndScopeFinalizer)));

        // --- (b) post-commit funnels recorded with status "committed" ---
        // (MoveToMapCoordAction.ExecuteAction is deliberately NOT patched: the umbrella
        // RequestEnqueue funnel already records move_to_map_coord for the same action
        // instance, and the human decision carries human=true on vote_for_map_coord —
        // a second travel_to_map_coord record double-counted every floor transition.)
        RecorderMod.TryPatch(
            harmony,
            "patch:NEventRoom.OptionButtonClicked",
            () => AccessTools.Method(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(EventOptionClickedPrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:EventSynchronizer.SaveEventOptionToHistory",
            () => AccessTools.Method(typeof(EventSynchronizer), "SaveEventOptionToHistory"),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(SaveEventOptionPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:MerchantEntry.OnTryPurchaseWrapper",
            () => AccessTools.Method(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(MerchantPurchasePrefix)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(MerchantPurchasePostfix)));
        // Method HIDING (not override): the card-removal overload must be patched separately.
        // Explicit parameter types: the class sees BOTH its own hiding 3-arg overload and
        // the inherited 2-arg base method — a name-only lookup is ambiguous (v0.107.1).
        RecorderMod.TryPatch(
            harmony,
            "patch:MerchantCardRemovalEntry.OnTryPurchaseWrapper",
            () => AccessTools.Method(
                typeof(MerchantCardRemovalEntry),
                nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper),
                new[] { typeof(MerchantInventory), typeof(bool), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(MerchantPurchasePrefix)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(CardRemovalPurchasePostfix)));
        // v0.107.1 reward claiming is synchronized (Reward.OnSelectWrapper removed):
        //   * RewardsSetSynchronizer.SelectLocalReward = the LOCAL player's explicit UI
        //     claim (NRewardButton) — prefix only MARKS the reward as human-initiated.
        //   * Reward.SelectUnsynchronized = the executed claim on EVERY machine (local,
        //     remote, and programmatic auto-claims like the Draft modifier) — the single
        //     committed "reward_taken" record, filtered to the local player and tagged
        //     human/programmatic via the mark. One record per claim, no double-count.
        RecorderMod.TryPatch(
            harmony,
            "patch:RewardsSetSynchronizer.SelectLocalReward",
            () => AccessTools.Method(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SelectLocalRewardPrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:Reward.SelectUnsynchronized",
            () => AccessTools.Method(typeof(Reward), nameof(Reward.SelectUnsynchronized)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(RewardSelectPrefix)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(RewardSelectPostfix)));
        // v0.107.1: reward-set skips are synchronized too. SkipLocalRewardsSet is the
        // ONE local human skip decision; the per-subclass Reward.OnSkipped overrides now
        // fire on all machines for every unselected reward (RewardsSetSynchronizer.
        // SkipRewardsSet) and are no longer patched — one decision, one record.
        RecorderMod.TryPatch(
            harmony,
            "patch:RewardsSetSynchronizer.SkipLocalRewardsSet",
            () => AccessTools.Method(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SkipLocalRewardsSet)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SkipLocalRewardsSetPrefix)));
        // R4 reroll results: CardReward.Reroll() repopulates the offer in place
        // (CardReward.cs:761); the postfix records the new cards.
        RecorderMod.TryPatch(
            harmony,
            "patch:CardReward.Reroll",
            () => AccessTools.Method(typeof(CardReward), nameof(CardReward.Reroll)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(CardRewardRerollPostfix)));
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
            () => AccessTools.Method(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice)),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(SyncLocalChoicePrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:PlayerChoiceContext.PushModel",
            () => AccessTools.Method(typeof(PlayerChoiceContext), nameof(PlayerChoiceContext.PushModel)),
            postfix: new HarmonyMethod(typeof(HumanFunnels), nameof(PushModelPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:NTreasureRoom.OnChestButtonReleased",
            () => AccessTools.Method(typeof(NTreasureRoom), "OnChestButtonReleased"),
            prefix: new HarmonyMethod(typeof(HumanFunnels), nameof(ChestButtonPrefix)));
    }

    private static void PatchSyncLocal(Harmony harmony, string methodName, string patchName)
    {
        RecorderMod.TryPatch(
            harmony,
            $"patch:RewardSynchronizer.{methodName}",
            () => AccessTools.Method(typeof(RewardSynchronizer), methodName),
            prefix: new HarmonyMethod(typeof(HumanFunnels), patchName));
    }

    /// <summary>RestSiteSynchronizer is recreated each run — resubscribe on every RunStarted.</summary>
    internal static void OnRunStarted(TrajectorySession session)
    {
        DecisionHooks.Reset();
        _restDecisions = new();
        _eventDecisions = new();
        _lastChoiceContextModel = null;
        _locallySelectedRewards = new();
        _cardRewardOffers = new ConditionalWeakTable<CardReward, List<CardModel>>();
        try
        {
            var synchronizer = RunManager.Instance.RestSiteSynchronizer;
            _restSiteSession = session;
            _restSiteHandler = OnRestSiteOptionChosen;
            _restSiteBeforeHandler = OnBeforeRestSiteOptionChosen;
            synchronizer.BeforePlayerOptionChosen += _restSiteBeforeHandler;
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
            if (_restSiteBeforeHandler != null)
                RunManager.Instance.RestSiteSynchronizer.BeforePlayerOptionChosen -= _restSiteBeforeHandler;
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
        _restSiteBeforeHandler = null;
        _restDecisions = new();
        _eventDecisions = new();
        DecisionHooks.Reset();
        _restSiteSession = null;
        _lastChoiceContextModel = null;
        _locallySelectedRewards = new();
        _cardRewardOffers = new ConditionalWeakTable<CardReward, List<CardModel>>();
    }

    private static void RecordCommitted(string source, string kind, JsonObject parameters,
        DecisionCapture? decision = null)
    {
        try
        {
            if (decision != null && !ReferenceEquals(decision.Session, RecorderMod.Session)) return;
            if (decision != null)
                decision.Record(RecorderMod.Session, source, kind, parameters);
            else
                RecorderMod.Session?.RecordAction(source, kind, parameters, "committed", null);
            Snapshots.RequestDeferredSnapshot();
        }
        catch (Exception ex)
        {
            RecorderMod.Report($"HumanFunnels.RecordCommitted:{kind}", ex);
        }
    }

    /// <summary>
    /// v0.107.1: SyncLocalObtained*/Skipped* is now effectively the MERCHANT channel
    /// (reward screens moved to RewardsSetSynchronizer) — the shop-context dedupe keeps
    /// purchases single-recorded by the merchant purchase hook; the remaining non-shop
    /// caller (CrystalSphereCurse gaining Doubt) still records here.
    /// </summary>
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
        ActionPipeline.BeginHumanScope("hook:CardModel.TryManualPlay", combatInput: true);
    }

    private static void CallReleaseLogicPrefix()
    {
        // The enqueued type disambiguates: IsPlayerReadyToEndTurn false -> EndPlayerTurnAction,
        // true -> UndoEndPlayerTurnAction (the game reads it inside CallReleaseLogic itself).
        ActionPipeline.BeginHumanScope("hook:NEndTurnButton.CallReleaseLogic", combatInput: true);
    }

    private static void SecretEndTurnPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:NEndTurnButton.SecretEndTurnLogicViaFtue", combatInput: true);
    }

    private static void EnqueueManualUsePrefix(PotionModel __instance)
    {
        try
        {
            // Local machine only: remote uses arrive via net messages, not this method.
            if (LocalContext.IsMe(__instance.Owner))
            {
                ActionPipeline.BeginHumanScope("hook:PotionModel.EnqueueManualUse", combatInput: IsCombatInProgress());
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.EnqueueManualUsePrefix", ex);
        }
    }

    private static void DiscardPotionPrefix()
    {
        ActionPipeline.BeginHumanScope("hook:NPotionPopup.OnDiscardButtonPressed", combatInput: IsCombatInProgress());
    }

    /// <summary>Potion use/discard is a combat input (gated capture) only while a combat runs.</summary>
    private static bool IsCombatInProgress()
    {
        try
        {
            return CombatManager.Instance.IsInProgress;
        }
        catch
        {
            return true; // unknown: treat as combat, i.e. never add an ungated capture
        }
    }

    private static void MapPointSelectedPrefix(NMapScreen __instance)
    {
        ActionPipeline.BeginHumanScope("hook:NMapScreen.OnMapPointSelectedLocally", __instance);
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

    /// <summary>
    /// IsProceed options run option.Chosen() directly and bypass the synchronizer —
    /// this prefix is the only capture for proceed clicks (and for options with
    /// ShouldSaveChoiceToHistory == false the game itself never records).
    /// </summary>
    private static void EventOptionClickedPrefix(NEventRoom __instance, EventOption option, int index)
    {
        try
        {
            if (RecorderMod.Session == null || option == null || option.IsLocked)
            {
                return;
            }
            if (!option.IsProceed)
            {
                // Real choices are committed via SaveEventOptionToHistory; pin the
                // pre-click observation and the clicked index for that record.
                var capture = Snapshots.CaptureDecision(__instance);
                if (capture != null) _eventDecisions.AddOrUpdate(option, new EventInput(capture, index));
                return;
            }
            var before = Snapshots.CaptureDecision(__instance);
            RecordCommitted("hook:NEventRoom.OptionButtonClicked", "event_proceed", new JsonObject
            {
                ["index"] = index,
                ["text_key"] = JsonDescribe.Try(() => option.TextKey),
                ["title"] = JsonDescribe.Try(() => option.Title?.GetRawText()),
            }, before);
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
            _eventDecisions.TryGetValue(option, out var input);
            _eventDecisions.Remove(option);
            var before = input?.Capture ?? new DecisionCapture(RecorderMod.Session, 0, "missing_event_input");
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
                ["text_key"] = JsonDescribe.Try(() => option.TextKey),
                ["relic_id"] = JsonDescribe.Try(() => option.Relic?.Id.ToString()),
                ["index"] = input?.Index,
            }, before);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.SaveEventOptionPostfix", ex);
        }
    }

    private static void OnBeforeRestSiteOptionChosen(RestSiteOption option, ulong playerId)
    {
        try
        {
            if (playerId != LocalContext.NetId || RecorderMod.Session != _restSiteSession) return;
            var before = Snapshots.CaptureDecision(NRestSiteRoom.Instance);
            if (before != null) _restDecisions.AddOrUpdate(option, before);
        }
        catch (Exception ex) { RecorderMod.Report("HumanFunnels.BeforeRestSite", ex); }
    }

    private static void OnRestSiteOptionChosen(RestSiteOption option, bool success, ulong playerId)
    {
        try
        {
            if (_restSiteSession == null || RecorderMod.Session != _restSiteSession)
            {
                return;
            }
            if (playerId != LocalContext.NetId) return;
            _restDecisions.TryGetValue(option, out var before);
            _restDecisions.Remove(option);
            if (!success)
            {
                return; // success=false means the sub-flow was backed out (no commitment).
            }
            RecordCommitted("event:RestSiteSynchronizer.AfterPlayerOptionChosen", "rest_site_option", new JsonObject
            {
                ["option_id"] = JsonDescribe.Try(() => option.OptionId),
                ["player"] = (long)playerId,
            }, before ?? new DecisionCapture(_restSiteSession, 0, "missing_rest_input"));
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
    private static void MerchantPurchasePrefix(MerchantEntry __instance, bool ignoreCost, out PurchaseInput? __state)
    {
        __state = null;
        try
        {
            var before = Snapshots.CaptureDecision(null);
            if (before == null) return;
            var parameters = DescribeMerchantEntry(__instance);
            parameters["ignore_cost"] = ignoreCost;
            __state = new PurchaseInput(before, parameters);
        }
        catch (Exception ex) { RecorderMod.Report("HumanFunnels.BeforePurchase", ex); }
    }

    private static void MerchantPurchasePostfix(Task<bool> __result, PurchaseInput? __state)
    {
        RecordPurchaseWhenComplete(__state, __result, "shop_purchase",
            "hook:MerchantEntry.OnTryPurchaseWrapper");
    }

    private static void CardRemovalPurchasePostfix(Task<bool> __result, PurchaseInput? __state)
    {
        RecordPurchaseWhenComplete(__state, __result, "shop_card_removal",
            "hook:MerchantCardRemovalEntry.OnTryPurchaseWrapper");
    }

    private static void RecordPurchaseWhenComplete(
        PurchaseInput? input, Task<bool>? result, string kind, string source)
    {
        try
        {
            if (RecorderMod.Session == null || result == null || input == null)
            {
                return;
            }
            result.ContinueWith(task =>
            {
                try
                {
                    if (task.Status != TaskStatus.RanToCompletion || !task.Result)
                    {
                        return;
                    }
                    RecorderMod.RunOnMainThread(() => RecordCommitted(source, kind, input.Parameters, input.Capture));
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

    /// <summary>
    /// LOCAL-intent marker (v0.107.1): SelectLocalReward is only reachable from the local
    /// player's own UI claim (NRewardButton; the RewardsSet.Offer caller is TestMode-only,
    /// and recording never runs under TestMode). No record here — SelectUnsynchronized
    /// carries the committed record and reads this mark for human attribution.
    /// </summary>
    private static void SelectLocalRewardPrefix(Reward reward)
    {
        try
        {
            if (RecorderMod.Session == null || reward == null)
            {
                return;
            }
            var before = Snapshots.CaptureDecision(null);
            if (before != null) _locallySelectedRewards.AddOrUpdate(reward, before);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.SelectLocalRewardPrefix", ex);
        }
    }

    /// <summary>
    /// Non-virtual async wrapper — one patch covers all Reward subclasses. Executes on
    /// EVERY machine (local + remote claims) and for programmatic auto-claims (Draft
    /// run-start modifier), so filter to the local player and tag the source.
    /// </summary>
    private static void RewardSelectPrefix(Reward __instance, out DecisionCapture? __state)
    {
        __state = null;
        try
        {
            if (RecorderMod.Session == null || !LocalContext.IsMe(__instance.Player)) return;
            __state = _locallySelectedRewards.TryGetValue(__instance, out var before)
                ? before : Snapshots.CaptureDecision(null);
        }
        catch (Exception ex) { RecorderMod.Report("HumanFunnels.BeforeReward", ex); }
    }

    private static void RewardSelectPostfix(Reward __instance, Task<bool> __result, DecisionCapture? __state)
    {
        try
        {
            if (RecorderMod.Session == null || __result == null
                || !JsonDescribe.Try<bool?>(() => LocalContext.IsMe(__instance.Player)).GetValueOrDefault())
            {
                return;
            }
            // Attribution decided now (the mark was set synchronously before this call);
            // consume the mark so it cannot leak onto a later claim.
            var human = _locallySelectedRewards.TryGetValue(__instance, out _);
            if (human)
            {
                _locallySelectedRewards.Remove(__instance);
            }
            // R4: the offer as it stands when the selection starts (the first await
            // inside OnSelect is the player's pick, so nothing has been taken yet).
            var offer = RewardOffers.Describe(__instance);
            if (__instance is CardReward offeredCardReward)
            {
                var offeredCards = JsonDescribe.Try(() => offeredCardReward.Cards.ToList()) ?? new List<CardModel>();
                _cardRewardOffers.AddOrUpdate(offeredCardReward, offeredCards);
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
                    RecorderMod.RunOnMainThread(() =>
                    {
                        if (__state == null || !ReferenceEquals(__state.Session, RecorderMod.Session)) return;
                        var parameters = DescribeReward(__instance);
                        parameters["human"] = human;
                        parameters["programmatic"] = !human; // e.g. Draft modifier auto-claims
                        parameters["offer"] = offer;
                        if (__instance is CardReward takenCardReward)
                        {
                            parameters["picked_cards"] = PickedCards(takenCardReward);
                        }
                        RecordCommitted("hook:Reward.SelectUnsynchronized", "reward_taken", parameters, __state);
                    });
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

    /// <summary>Offered cards (initial offer and rerolls) that are now in the master deck.</summary>
    private static JsonArray PickedCards(CardReward cardReward)
    {
        var picked = new JsonArray();
        try
        {
            if (!_cardRewardOffers.TryGetValue(cardReward, out var offered))
            {
                return picked;
            }
            _cardRewardOffers.Remove(cardReward);
            var deck = new HashSet<CardModel>(cardReward.Player.Deck.Cards, ReferenceEqualityComparer.Instance);
            foreach (var card in offered)
            {
                if (deck.Contains(card))
                {
                    picked.Add(RewardOffers.CardIdentity(card));
                }
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.PickedCards", ex);
        }
        return picked;
    }

    /// <summary>
    /// R4: the rerolled card offer (CardReward.Reroll repopulates in place). Recorded on
    /// the events stream; a deferred snapshot also captures the refreshed card-reward screen.
    /// </summary>
    private static void CardRewardRerollPostfix(CardReward __instance)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null || __instance == null
                || !JsonDescribe.Try<bool?>(() => LocalContext.IsMe(__instance.Player)).GetValueOrDefault())
            {
                return;
            }
            var cards = JsonDescribe.Try(() => __instance.Cards.ToList()) ?? new List<CardModel>();
            if (_cardRewardOffers.TryGetValue(__instance, out var offered))
            {
                offered.AddRange(cards);
            }
            session.RecordEvent("card_reward_rerolled", new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(() => (long)__instance.Player.NetId),
                ["cards"] = RewardOffers.CardIdentities(cards),
                ["can_reroll"] = JsonDescribe.Try<bool?>(() => __instance.CanReroll),
            });
            Snapshots.RequestDeferredSnapshot();
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.CardRewardRerollPostfix", ex);
        }
    }

    /// <summary>
    /// The ONE local human skip decision for the whole rewards set (NRewardsScreen skip
    /// button). The unselected items are visible in the preceding rewards-screen state
    /// snapshot; per-reward OnSkipped callbacks are deliberately not recorded.
    /// </summary>
    private static void SkipLocalRewardsSetPrefix()
    {
        try
        {
            if (RecorderMod.Session == null)
            {
                return;
            }
            RecordCommitted("hook:RewardsSetSynchronizer.SkipLocalRewardsSet", "rewards_skipped", new JsonObject
            {
                ["player"] = JsonDescribe.Try<long?>(
                    () => LocalContext.NetId is { } netId ? (long)netId : null),
                ["human"] = true,
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("HumanFunnels.SkipLocalRewardsSetPrefix", ex);
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

    // --- RewardSynchronizer.SyncLocal* prefixes ---
    // v0.107.1 caller-set drift: reward screens NO LONGER call these; remaining callers
    // are merchant purchases (deduped via IsShopContext) and CrystalSphereCurse.

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
            RecordCommitted("hook:PlayerChoiceSynchronizer.SyncLocalChoice", "player_choice", parameters,
                DecisionHooks.Consume(player.NetId, choiceId));
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
                    var instances = new JsonArray();
                    var count = 0;
                    var models = JsonDescribe.Try<IEnumerable<CardModel>>(() => result.AsCards(result.ChoiceType));
                    if (models != null)
                    {
                        foreach (var card in models)
                        {
                            cards.Add(JsonDescribe.Model(card));
                            instances.Add(new JsonObject
                            {
                                ["id"] = JsonDescribe.Model(card),
                                ["instance_id"] = PassiveStateBuilder.CardInstanceId(card),
                            });
                            count++;
                        }
                    }
                    parameters["cards"] = cards;
                    parameters["card_instances"] = instances;
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
