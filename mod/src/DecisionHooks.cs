using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// Observes decision surfaces at readiness/refresh and immediately BEFORE UI input
/// resolves a TaskCompletionSource or clears the candidates. No game/UI mutations.
/// SyncLocalChoice can run after the overlay was removed, hence the per-choice pin.
/// </summary>
public static class DecisionHooks
{
    private static DecisionChoices _choices = new();

    internal static void Reset() => _choices = new DecisionChoices();

    internal sealed record HookTarget(Type Type, string Method, string? Prefix, string? Postfix, string? Finalizer);

    // Pure metadata: the offline contract check resolves every target against the
    // installed game assembly without initializing Godot or applying a patch.
    internal static IReadOnlyList<HookTarget> GetPatchPlan()
    {
        var targets = new List<HookTarget>();
        void Add(Type type, string method, string? prefix = null, string? postfix = null) =>
            targets.Add(new HookTarget(type, method, prefix, postfix,
                prefix == nameof(ChoiceInputPrefix) ? nameof(ChoiceInputFinalizer) : null));

        Add(typeof(PlayerChoiceSynchronizer), "ReserveChoiceId", postfix: nameof(ReservePostfix));
        Add(typeof(NCardRewardSelectionScreen), "OptionSelected", prefix: nameof(ChoiceOpenedPrefix));
        Add(typeof(NCardRewardSelectionScreen), "RefreshOptions", postfix: nameof(SurfaceChangedPostfix));
        Add(typeof(NCardRewardSelectionScreen), "SelectCard", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NCardGridSelectionScreen), "CardsSelected", prefix: nameof(ChoiceOpenedPrefix));
        foreach (var (type, completionMethods) in new[]
        {
            (typeof(NDeckUpgradeSelectScreen), new[] { "ConfirmSelection", "CloseSelection", "CancelSelection", "CheckIfSelectionComplete" }),
            (typeof(NDeckTransformSelectScreen), new[] { "ConfirmSelection", "CloseSelection", "CancelSelection", "CompleteSelection" }),
            (typeof(NDeckCardSelectScreen), new[] { "ConfirmSelection", "CloseSelection", "CancelSelection", "CheckIfSelectionComplete" }),
            (typeof(NDeckEnchantSelectScreen), new[] { "ConfirmSelection", "CloseSelection", "CancelSelection", "CheckIfSelectionComplete" }),
            (typeof(NSimpleCardSelectScreen), new[] { "CheckIfSelectionComplete", "CompleteSelection" }),
            (typeof(NCombatPileCardSelectScreen), new[] { "CheckIfSelectionComplete", "CompleteSelection" }),
        })
        {
            Add(type, "OnCardClicked", prefix: nameof(ChoiceInputPrefix), postfix: nameof(ChoiceChangedPostfix));
            // Explicit v0.107.1 contract: a renamed completion method must degrade
            // visibly, rather than silently disappearing from a reflection scan.
            foreach (string name in completionMethods)
                Add(type, name, prefix: nameof(ChoiceInputPrefix), postfix: nameof(ChoiceChangedPostfix));
        }
        Add(typeof(NCombatPileCardSelectScreen), "UpdatePileContents", postfix: nameof(BoundSurfaceChangedPostfix));
        Add(typeof(NChooseACardSelectionScreen), "CardsSelected", prefix: nameof(ChoiceOpenedPrefix));
        Add(typeof(NChooseACardSelectionScreen), "SelectHolder", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NChooseACardSelectionScreen), "OnSkipButtonReleased", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NChooseARelicSelection), "RelicsSelected", prefix: nameof(ChoiceOpenedPrefix));
        Add(typeof(NChooseARelicSelection), "SelectHolder", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NChooseARelicSelection), "OnSkipButtonReleased", prefix: nameof(ChoiceInputPrefix));
        Add(typeof(NChooseABundleSelectionScreen), "CardsSelected", prefix: nameof(ChoiceOpenedPrefix));
        foreach (string name in new[] { "OnBundleClicked", "CancelSelection", "ConfirmSelection" })
            Add(typeof(NChooseABundleSelectionScreen), name,
                prefix: nameof(ChoiceInputPrefix), postfix: nameof(ChoiceChangedPostfix));
        // SelectCards is async: postfix runs after mode/filter/prefs are initialized,
        // at its first await. Capture the selection before any input can resolve it.
        Add(typeof(NPlayerHand), "SelectCards", postfix: nameof(ChoiceOpenedPrefix));
        foreach (string name in new[] { "SelectCardInSimpleMode", "SelectCardInUpgradeMode",
                     "DeselectCard", "OnSelectModeConfirmButtonPressed", "CheckIfSelectionComplete" })
            Add(typeof(NPlayerHand), name,
                prefix: nameof(ChoiceInputPrefix), postfix: nameof(ChoiceChangedPostfix));
        Add(typeof(NEventRoom), "SetOptions", postfix: nameof(SurfaceChangedPostfix));
        Add(typeof(NMapScreen), "Open", postfix: nameof(SurfaceChangedPostfix));
        Add(typeof(NMapScreen), "RefreshAllPointVisuals", postfix: nameof(MapChangedPostfix));
        return targets;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        foreach (var target in GetPatchPlan())
            Patch(harmony, target.Type, target.Method, target.Prefix, target.Postfix, target.Finalizer);
    }

    private static void Patch(Harmony harmony, Type type, string method, string? prefix = null,
        string? postfix = null, string? finalizer = null)
    {
        RecorderMod.TryPatch(harmony, $"decision:{type.Name}.{method}",
            () => AccessTools.Method(type, method),
            prefix: prefix == null ? null : new HarmonyMethod(typeof(DecisionHooks), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(DecisionHooks), postfix),
            finalizer: finalizer == null ? null : new HarmonyMethod(typeof(DecisionHooks), finalizer));
    }

    private static void ReservePostfix(Player player, uint __result)
    {
        try
        {
            if (RecorderMod.Session != null && LocalContext.IsMe(player))
                _choices.Reserve(player.NetId, __result);
        }
        catch (Exception ex) { RecorderMod.Report("DecisionHooks.Reserve", ex); }
    }

    private static void ChoiceOpenedPrefix(object __instance)
    {
        try
        {
            if (RecorderMod.Session == null || LocalContext.NetId is not { } player) return;
            if (!_choices.Bind(__instance, player)) return;
            var capture = Snapshots.CaptureDecision(__instance, "opened");
            if (capture != null) _choices.Update(__instance, capture);
        }
        catch (Exception ex) { RecorderMod.Report("DecisionHooks.Opened", ex); }
    }

    private static void ChoiceInputPrefix(object __instance, out DecisionChoices.InputScope? __state)
    {
        __state = null;
        try
        {
            __state = _choices.BeginInput(__instance);
            if (__state == null) return;
            RecorderMod.Session?.NoteDecisionInput(nested: !__state.ShouldCapture);
            if (!__state.ShouldCapture) return;
            var capture = Snapshots.CaptureDecision(__instance);
            if (capture != null) _choices.Update(__instance, capture);
        }
        catch (Exception ex) { RecorderMod.Report("DecisionHooks.Input", ex); }
    }

    private static void ChoiceChangedPostfix(object __instance, DecisionChoices.InputScope? __state)
    {
        // Preserve the pre-input pin. Completion may have removed the screen and
        // consumed its choice synchronously inside the original method.
        if (__state is { ShouldCapture: true } && _choices.IsCurrent(__instance, __state))
            SurfaceChangedPostfix(__instance);
    }

    // Harmony finalizers also run on exceptions. Never leave an input marked active
    // after a failed UI handler; never suppress/replace the game's own exception.
    private static void ChoiceInputFinalizer(DecisionChoices.InputScope? __state) => __state?.Dispose();

    private static void SurfaceChangedPostfix(object __instance)
    {
        Snapshots.CaptureDecision(__instance, "updated");
    }

    private static void BoundSurfaceChangedPostfix(object __instance)
    {
        if (_choices.IsBound(__instance)) SurfaceChangedPostfix(__instance);
    }

    private static void MapChangedPostfix(NMapScreen __instance)
    {
        if (__instance.IsOpen) SurfaceChangedPostfix(__instance);
    }

    internal static DecisionCapture? Consume(ulong player, uint choiceId)
    {
        var capture = _choices.Consume(player, choiceId);
        return capture ?? (RecorderMod.Session is { } session
            ? new DecisionCapture(session, 0, "unbound_choice") : null);
    }
}
