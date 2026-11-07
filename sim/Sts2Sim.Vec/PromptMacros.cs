using Sts2Sim.Core;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// Prompt macros v2 (Vec layer only, opt-in with <c>config_json</c> <c>"macros": ["prompt"]</c> or <c>"all"</c>; W4
/// 2026-11-04; docs/prompt-macros-v2-2026-11-04.md). Off (the default) nothing here runs. On, every pending choice prompt
/// (hand / pile / choose-a-card / grid / deck upgrade, transform, enchant, remove / bundle; combat, events, rooms, rewards,
/// relics, potions) is answered without a way back to the screen that opened it:
/// <list type="bullet">
/// <item><b>No cancel row.</b> <c>PromptCancel</c> is never listed (the four cancelable sites of the port — SMITH and COOK
/// at a rest site, the merchant's removal, the RemoveCard reward — all return to the identical screen they were opened
/// from, the loop of docs/diag-2026-11-04/q4-loopbreaker.md). Declining is offered only where the game's own prompt allows
/// it: the <c>PromptConfirm</c> row with zero picks (MinSelect 0 / CanSkip) is the explicit DECLINE; with picks it is DONE.
/// If a prompt listed nothing but a cancel the cancel row would stay (never observed in the port).</item>
/// <item><b>A pick commits as soon as the selection is complete.</b> Executing a <c>PromptSelect</c> row runs the game's
/// pick; when the same prompt is still pending afterwards and its only non-cancel legal action is <c>PromptConfirm</c>
/// (exactly-1 / exactly-k with manual confirmation, or the last pick of an up-to-k prompt), the confirm click runs in the
/// same step. Exactly-1 prompts are one decision (one row per legal option); exactly-k prompts are k decisions that commit
/// at the k-th pick (picks are monotone — the game has no deselect — so no state repeats); up-to-N prompts are picks plus
/// the explicit DONE row.</item>
/// <item><b>Only selectable options.</b> The rows are the game's own <c>PromptSelect</c> legal actions over the prompt's
/// options, which the game built with its selectability filter (e.g. HAND_TRICK: skills in hand that are not sly this
/// turn; SECRET_TECHNIQUE: skills in the draw pile; deck prompts: IsUpgradable / IsRemovable / IsTransformable /
/// CanEnchant); nothing is added and nothing hidden is shown.</item>
/// <item><b>Row cap.</b> With <c>prompt</c> on, the rest / shop macro rows (<see cref="RoomMacros"/>) fall back to the
/// plain SMITH / removal row (then the sequential no-cancel prompt) when the expansion would list more than
/// <see cref="MaxRows"/> candidates (the trainer's <c>--max-rows</c> default); that observation's status carries
/// <see cref="SlotStatus.MacroFallback"/>.</item>
/// </list>
/// Candidate rows keep the published kinds and columns (PromptSelect / PromptConfirm), so the schema, the RunActionKind
/// enum, the encoder_version and the ModelSpec are identical with the macros on or off.
/// </summary>
public static class PromptMacros
{
    /// <summary>The row cap of the room macro fallback: the trainer's default <c>--max-rows</c>.</summary>
    public const int MaxRows = 64;

    [ThreadStatic]
    private static List<RunAction>? _scratch;

    /// <summary>
    /// Removes the <c>PromptCancel</c> rows of a pending prompt's candidate list <paramref name="cands"/> in place (no-op
    /// without a pending prompt, or when nothing but a cancel is listed).
    /// </summary>
    public static void Apply(RunSim sim, List<RunAction> cands)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(cands);
        if (!sim.HasPendingChoice || sim.IsTerminal)
        {
            return;
        }
        int cancels = 0;
        for (int j = 0; j < cands.Count; j++)
        {
            if (cands[j].Kind == RunActionKind.PromptCancel)
            {
                cancels++;
            }
        }
        if (cancels == 0 || cancels == cands.Count)
        {
            return;
        }
        cands.RemoveAll(a => a.Kind == RunActionKind.PromptCancel);
    }

    /// <summary>
    /// Every macro substitution of <paramref name="set"/> on the Vec candidate list <paramref name="cands"/> (after
    /// <see cref="RewardMacros.Fill"/>): the room macros, and with <see cref="RoomMacroSet.Prompt"/> the row cap and the
    /// prompt rules. True when a room macro fell back to its plain row (<see cref="SlotStatus.MacroFallback"/>).
    /// </summary>
    public static bool ApplyAll(RunSim sim, List<RunAction> cands, RoomMacroSet set)
    {
        if ((set & RoomMacroSet.Prompt) == 0)
        {
            RoomMacros.Apply(sim, cands, set);
            return false;
        }
        RoomMacros.Apply(sim, cands, set, MaxRows, out bool fellBack);
        Apply(sim, cands);
        return fellBack;
    }

    /// <summary>The Vec candidate list of <paramref name="sim"/> with macros <paramref name="set"/> (a new list; tests and tools).</summary>
    public static List<RunAction> Candidates(RunSim sim, RoomMacroSet set)
    {
        var cands = RewardMacros.Candidates(sim);
        ApplyAll(sim, cands, set);
        return cands;
    }

    /// <summary>True when <paramref name="sim"/>'s pending prompt can only go on by <c>PromptConfirm</c> (cancel aside).</summary>
    public static bool OnlyConfirmLeft(RunSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (!sim.HasPendingChoice)
        {
            return false;
        }
        var legal = _scratch ??= new List<RunAction>(32);
        sim.LegalActions(legal);
        bool confirm = false;
        foreach (var a in legal)
        {
            if (a.Kind == RunActionKind.PromptConfirm)
            {
                confirm = true;
            }
            else if (a.Kind != RunActionKind.PromptCancel)
            {
                return false;
            }
        }
        return confirm;
    }

    /// <summary>
    /// Runs the pick <paramref name="pick"/> (a <c>PromptSelect</c> row) through <paramref name="apply"/> (the env's
    /// one-action step), then the game's <c>PromptConfirm</c> when the same prompt is still pending and Confirm is its
    /// only way on. Returns the number of game actions applied (1 or 2). A prompt the game opens afterwards is left for
    /// the policy.
    /// </summary>
    public static int ExecuteSelect(RunSim sim, RunAction pick, Action<RunAction> apply)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(apply);
        if (pick.Kind != RunActionKind.PromptSelect)
        {
            throw new ArgumentException($"{pick} is not a PromptSelect", nameof(pick));
        }
        var prompt = sim.PendingChoice ?? throw new InvalidOperationException($"prompt macro {pick}: no prompt is pending");
        apply(pick);
        if (!ReferenceEquals(sim.PendingChoice, prompt) || !OnlyConfirmLeft(sim))
        {
            return 1;
        }
        apply(RunAction.Of(RunActionKind.PromptConfirm));
        if (ReferenceEquals(sim.PendingChoice, prompt))
        {
            throw new InvalidOperationException($"prompt macro {pick}: {prompt.SiteId} is still pending after PromptConfirm");
        }
        return 2;
    }
}
