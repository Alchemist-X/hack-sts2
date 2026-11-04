using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// Rewards-screen macro candidates (Vec layer only; the deck-building channel, 2026-11-04). On the rewards screen (loop phase
/// Rewards, no pending prompt) the candidate list is <c>RunSim.LegalActions()</c> with two substitutions, so that one
/// decision does what a human does with the cards in view:
/// <list type="bullet">
/// <item><c>ClaimReward(r)</c> of an unclaimed card reward r becomes one <see cref="TakeRewardCard"/> row (A = r, B = card i)
/// per offered card, then one <see cref="TakeRewardAlternative"/> row (A = r, B = alternative k) per alternative the
/// opened screen would offer other than Skip (REROLL with Driftwood, SACRIFICE with Pael's Wing). A row expands to the
/// game's own two clicks: <c>ClaimReward(r)</c> (opens the card screen: <c>CardReward.OnSelectWrapper</c>, game
/// Rewards/CardReward.cs:592-603) then <c>PickRewardCard(i)</c> / <c>RewardAlternative(k)</c> (:604-700), so the run
/// takes exactly the legal game sequence and replays deterministically.</item>
/// <item><c>ClaimReward(r)</c> of a potion reward the game would refuse — a full belt, <c>TryToProcure</c> fails with
/// TooFull and <c>OnSelect</c> returns false with the reward unclaimed (game Rewards/PotionReward.cs:180-191) — is not
/// listed (<see cref="PotionReward.ClaimWouldBeRefused"/>). Gold, relic, special-card, removal and allowed potion claims
/// keep their one-click <c>ClaimReward</c> rows.</item>
/// </list>
/// The card screen's Skip is not offered here: it closes the screen without completing the reward
/// (EndSelectionAndDoNotCompleteReward, game Entities.CardRewardAlternatives/CardRewardAlternative.cs:67-73; OnSelect
/// returns rewardComplete = false, Rewards/CardReward.cs:660, :738), so the reward would stay on the rewards screen. Declining a card is
/// <c>ProceedRewards</c>, which skips every unclaimed reward (<c>OnSkipped</c>). Every other screen lists
/// <c>RunSim.LegalActions()</c> unchanged — including the CardReward phase (after a REROLL, or when a reward allows more
/// picks) with its granular PickRewardCard / RewardAlternative rows (Skip included).
/// </summary>
public static class RewardMacros
{
    /// <summary>The first Vec-only candidate kind: <c>RunActionKind</c> values below it are RunSim actions.</summary>
    public static readonly int FirstMacroKind = Enum.GetValues<RunActionKind>().Length;

    /// <summary>Open card reward A and take its card B (ClaimReward(A), PickRewardCard(B)).</summary>
    public static readonly RunActionKind TakeRewardCard = (RunActionKind)FirstMacroKind;

    /// <summary>Open card reward A and choose its alternative B, never Skip (ClaimReward(A), RewardAlternative(B)).</summary>
    public static readonly RunActionKind TakeRewardAlternative = (RunActionKind)(FirstMacroKind + 1);

    /// <summary>Names of the macro kinds, appended to the published <c>RunActionKind</c> enum list (value = FirstMacroKind + i).</summary>
    public static readonly IReadOnlyList<string> KindNames = new[] { "TakeRewardCard", "TakeRewardAlternative" };

    /// <summary>One more than the largest candidate kind value.</summary>
    public static int KindCount => FirstMacroKind + KindNames.Count;

    public static bool IsMacro(RunActionKind kind) => (int)kind >= FirstMacroKind;

    /// <summary>
    /// One choice of an unopened card reward on the rewards screen: card <see cref="Option"/> of reward <see cref="Reward"/>
    /// (<see cref="Card"/> set), or its alternative <see cref="Option"/> (<see cref="AltId"/> set).
    /// </summary>
    public readonly record struct Choice(int Reward, int Option, CardInstance? Card, string? AltId);

    [ThreadStatic]
    private static List<Choice>? _choices;

    /// <summary>
    /// The choices of every unclaimed card reward of the top rewards set, in candidate order: rewards ascending, each
    /// reward's cards (offer order), then its alternatives other than Skip (the order <c>CardRewardAlternative.Generate</c>
    /// would give them when the reward opens; pure: the hooks only append to a fresh list). Row k of
    /// <c>reward_choices</c> and the k-th macro candidate are choice k.
    /// </summary>
    public static void Choices(RunState rs, List<Choice> into)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var set = RunLegalActionGenerator.TopRewards(rs);
        if (set == null)
        {
            return;
        }
        var rewards = set.Rewards;
        for (int r = 0; r < rewards.Count; r++)
        {
            if (rewards[r] is not CardReward card || card.SuccessfullySelected || card.IsSelecting)
            {
                continue;
            }
            var cards = card.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                into.Add(new Choice(r, i, cards[i].Card, null));
            }
            var alternatives = CardRewardAlternative.Generate(rs, card);
            for (int k = 0; k < alternatives.Count; k++)
            {
                if (alternatives[k].OptionId != CardRewardAlternative.SkipId)
                {
                    into.Add(new Choice(r, k, null, alternatives[k].OptionId));
                }
            }
        }
    }

    /// <summary>True on the rewards screen proper: loop phase Rewards and no pending prompt (the screen where macros apply).</summary>
    public static bool IsRewardsScreen(RunSim sim) => !sim.HasPendingChoice && sim.Loop.Phase == RunPhase.Rewards;

    /// <summary>
    /// The Vec candidate list of <paramref name="sim"/> from its legal actions <paramref name="legal"/> (same order, with the
    /// rewards-screen substitutions of the class summary). <paramref name="cands"/> is cleared first.
    /// </summary>
    public static void Fill(RunSim sim, List<RunAction> legal, List<RunAction> cands)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(cands);
        cands.Clear();
        if (!IsRewardsScreen(sim))
        {
            cands.AddRange(legal);
            return;
        }
        var rs = sim.State;
        var set = RunLegalActionGenerator.TopRewards(rs);
        var choices = _choices ??= new List<Choice>(16);
        Choices(rs, choices);
        int next = 0;
        for (int j = 0; j < legal.Count; j++)
        {
            var a = legal[j];
            if (a.Kind == RunActionKind.ClaimReward && set != null && (uint)a.A < (uint)set.Rewards.Count)
            {
                switch (set.Rewards[a.A])
                {
                    case CardReward:
                        // choices are grouped by reward in ascending order, as ClaimReward rows are listed
                        while (next < choices.Count && choices[next].Reward < a.A)
                        {
                            next++;
                        }
                        for (; next < choices.Count && choices[next].Reward == a.A; next++)
                        {
                            var c = choices[next];
                            cands.Add(RunAction.Of(c.Card != null ? TakeRewardCard : TakeRewardAlternative, a.A, c.Option));
                        }
                        continue;
                    case PotionReward potion when potion.ClaimWouldBeRefused(rs):
                        continue;
                }
            }
            cands.Add(a);
        }
    }

    /// <summary>The Vec candidate list of <paramref name="sim"/> (a new list; tests and tools).</summary>
    public static List<RunAction> Candidates(RunSim sim)
    {
        var cands = new List<RunAction>();
        Fill(sim, sim.LegalActions().ToList(), cands);
        return cands;
    }

    /// <summary>The two game actions a macro candidate stands for: open card reward A, then take its card / alternative B.</summary>
    public static (RunAction Open, RunAction Choose) Expand(RunAction macro)
    {
        if (macro.Kind == TakeRewardCard)
        {
            return (RunAction.Of(RunActionKind.ClaimReward, macro.A), RunAction.Of(RunActionKind.PickRewardCard, macro.B));
        }
        if (macro.Kind == TakeRewardAlternative)
        {
            return (RunAction.Of(RunActionKind.ClaimReward, macro.A), RunAction.Of(RunActionKind.RewardAlternative, macro.B));
        }
        throw new ArgumentException($"{macro} is not a reward macro", nameof(macro));
    }

    /// <summary>The expansion of any candidate: one RunAction for a game action, two for a macro.</summary>
    public static IReadOnlyList<RunAction> ExpandAll(RunAction candidate)
    {
        if (!IsMacro(candidate.Kind))
        {
            return new[] { candidate };
        }
        var (open, choose) = Expand(candidate);
        return new[] { open, choose };
    }

    /// <summary>
    /// After the macro's ClaimReward step: the card screen of reward A is open, no prompt is pending, and option B exists
    /// (a card, or an alternative other than Skip). Throws <see cref="InvalidOperationException"/> otherwise, which the env
    /// reports as SIM_ERROR (fail closed: a macro never improvises).
    /// </summary>
    public static void RequireOpened(RunSim sim, RunAction macro)
    {
        string? why = null;
        if (sim.HasPendingChoice)
        {
            why = "a prompt is pending";
        }
        else if (sim.Loop.Phase != RunPhase.CardReward || sim.Loop.OpenCardReward != macro.A)
        {
            why = $"phase {sim.Loop.Phase}, open card reward {sim.Loop.OpenCardReward}";
        }
        else if (RunLegalActionGenerator.OpenCardReward(sim.State, sim.Loop) is not { } reward)
        {
            why = "no open card reward";
        }
        else if (macro.Kind == TakeRewardCard ? (uint)macro.B >= (uint)reward.Cards.Count
                 : (uint)macro.B >= (uint)reward.Alternatives.Count || reward.Alternatives[macro.B].OptionId == CardRewardAlternative.SkipId)
        {
            why = $"option {macro.B} is not offered ({reward.Cards.Count} cards, {reward.Alternatives.Count} alternatives)";
        }
        if (why != null)
        {
            throw new InvalidOperationException($"reward macro {KindName(macro.Kind)}({macro.A}, {macro.B}) diverged after ClaimReward: {why}");
        }
    }

    /// <summary>The published name of a candidate kind (RunActionKind names, then the macro names).</summary>
    public static string KindName(RunActionKind kind)
        => IsMacro(kind) && (int)kind - FirstMacroKind < KindNames.Count ? KindNames[(int)kind - FirstMacroKind] : kind.ToString();
}
