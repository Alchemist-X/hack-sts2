using System.Collections.Concurrent;
using Sts2Sim.Core;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Tests;

/// <summary>
/// Clone-free stepping behind the static prompt-site predicate (RunSim.MayPrompt / StepFast, Choice/PromptSites.g.cs).
/// Fail-closed contract: (1) every prompt a random soak meets comes from a source id in the table; (2) MayPrompt never
/// clears an action that then prompts; (3) a clone-free step equals the tape step (same StateHash); (4) a prompt on the
/// clone-free path throws FastPathPromptException and poisons the run (never a silent answer).
/// </summary>
public static class ChoiceFastPathTests
{
    private static readonly string[] Characters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    private enum Policy
    {
        Uniform,
        Biased,
        BiasedBoosted,
    }

    private sealed class SoakTally
    {
        public long Steps;
        public long FastSteps;
        public long Prompts;
        public long Runs;
        public long Gaps;
        public long Errors;
        public readonly ConcurrentDictionary<string, int> Sources = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> Blockers = new(StringComparer.Ordinal);
        public readonly ConcurrentQueue<string> Unknown = new();
        public readonly ConcurrentQueue<string> Unsound = new();
        public readonly ConcurrentQueue<string> ErrorSamples = new();
    }

    /// <summary>
    /// 2,000 runs (5 characters x A0/A10 x three policies: uniform, play-biased, play-biased with 999 HP for depth),
    /// ported content, tape stepping. Every prompt's SourceId must be in PromptSites.PromptSourceIds and no prompt may
    /// follow an action MayPrompt cleared.
    /// </summary>
    public static void Test_Choice_FastPath_SoakPromptSourcesInTableAndPredicateSound()
    {
        var t = Soak(2000, sampleEvery: 0, out _);
        Report("soak", t);
        Assert.True(t.Unknown.IsEmpty, $"{t.Unknown.Count} prompts from sources missing in PromptSites.PromptSourceIds, e.g. {string.Join(" | ", t.Unknown.Take(5))}");
        Assert.True(t.Unsound.IsEmpty, $"{t.Unsound.Count} prompts after MayPrompt == false, e.g. {string.Join(" | ", t.Unsound.Take(5))}");
        Assert.True(t.Prompts > 1000, $"the soak met only {t.Prompts} prompts; it should exercise the choice runtime");
        Assert.True(t.FastSteps > t.Steps / 4, $"fast-path fraction {t.FastSteps}/{t.Steps} is implausibly low");
    }

    /// <summary>20,000 sampled fast-path steps: StepFastUnchecked on the run and Step (tape engine) on a clone agree on StateHash.</summary>
    public static void Test_Choice_FastPath_CloneFreeStepMatchesTapeStepOn20kSamples()
    {
        const int target = 20_000;
        var t = Soak(400, sampleEvery: 3, out long samples, sampleTarget: target);
        Report("hash", t);
        Assert.True(t.Unsound.IsEmpty, $"{t.Unsound.Count} mismatches, e.g. {string.Join(" | ", t.Unsound.Take(5))}");
        Assert.True(samples >= target, $"only {samples} fast-path samples compared (need {target})");
        Console.WriteLine($"     FastPath hash: {samples} clone-free steps matched the tape engine's StateHash");
    }

    /// <summary>A clone-free step that reaches a prompt throws FastPathPromptException with the site, and poisons the run.</summary>
    public static void Test_Choice_FastPath_PromptOnCloneFreeStepFailsClosed()
    {
        // Silent starts with SURVIVOR (OnPlay: discard 1 from hand); find a turn where playing it opens the prompt.
        for (int seed = 1; seed < 200; seed++)
        {
            var sim = RunSim.NewRun(new RunConfig("FPFAIL" + seed, "SILENT", 0) { Content = ContentFilter.PortedOnly });
            var rng = new System.Random(seed);
            var legal = new List<RunAction>();
            for (int n = 0; n < 400 && !sim.IsTerminal; n++)
            {
                sim.LegalActions(legal);
                if (sim.Phase == RunPhase.Combat && !sim.HasPendingChoice && sim.State.Combat is { } cs && cs.Player.Hand.Count >= 3)
                {
                    int idx = -1;
                    foreach (var a in legal)
                    {
                        if (a.Kind == RunActionKind.PlayCard && cs.Player.Hand[a.A].Id == "SURVIVOR")
                        {
                            idx = legal.IndexOf(a);
                            break;
                        }
                    }
                    if (idx >= 0)
                    {
                        var play = legal[idx];
                        Assert.True(sim.MayPrompt(play), "MayPrompt must flag playing SURVIVOR");
                        Assert.Equal(RunSim.BlockPlayedCard, sim.FastPathBlocker(play));
                        var probe = sim.Clone();
                        probe.Step(play);
                        if (!probe.HasPendingChoice)
                        {
                            sim.Step(play);
                            continue;
                        }
                        var ex = Assert.Throws<FastPathPromptException>(() => sim.StepFastUnchecked(play));
                        Assert.Equal("SURVIVOR", ex.Prompt.SourceId);
                        Assert.Equal(play, ex.Action!.Value);
                        Assert.True(ex.Message.Contains("SURVIVOR:OnPlay", StringComparison.Ordinal), ex.Message);
                        Assert.True(sim.IsPoisoned, "the run must be poisoned");
                        Assert.Throws<InvalidOperationException>(() => sim.Step(legal[0]), "a poisoned run cannot be stepped");
                        Assert.Throws<InvalidOperationException>(() => sim.Clone(), "a poisoned run cannot be cloned");
                        Assert.True(sim.MayPrompt(legal[0]), "a poisoned run never clears the fast path");
                        return;
                    }
                }
                sim.Step(legal[rng.Next(legal.Count)]);
            }
        }
        Assert.Fail("no seed reached a SURVIVOR play with a prompt");
    }

    /// <summary>The generated table is internally consistent with the predicate's assumptions.</summary>
    public static void Test_Choice_FastPath_TableShape()
    {
        Assert.True(PromptSites.CardsOnPlay.Contains("SURVIVOR"), "SURVIVOR prompts on play");
        Assert.True(PromptSites.Monsters.Contains("KNOWLEDGE_DEMON"), "KNOWLEDGE_DEMON prompts on its move");
        Assert.True(PromptSites.Powers.Contains("TOOLS_OF_THE_TRADE_POWER"), "TOOLS_OF_THE_TRADE_POWER prompts at turn start");
        Assert.True(PromptSites.CardsOnPlay.Contains("TOOLS_OF_THE_TRADE"), "closure: the card applies the power");
        Assert.True(PromptSites.RelicsPresence.Contains("GAMBLING_CHIP"), "GAMBLING_CHIP prompts at turn start");
        Assert.True(PromptSites.RelicsOnObtain.Contains("ASTROLABE"), "ASTROLABE prompts when obtained");
        Assert.True(PromptSites.PotionsOnUse.Contains("DISTILLED_CHAOS"), "DISTILLED_CHAOS auto-plays cards");
        foreach (var room in new[] { "SMITH", "COOK", "MERCHANT_CARD_REMOVAL", "COMBAT_REWARD_CARD_REMOVAL" })
        {
            Assert.True(PromptSites.RoomSources.Contains(room) && PromptSites.IsKnownSource(room), room);
        }
        Assert.False(PromptSites.CardsOnPlay.Overlaps(PromptSites.CardsPresence), "a card is play-gated or presence, not both");
        Assert.Equal(PromptSites.ModelCount, PromptSites.Sites.Select(s => s.Kind + ":" + s.ModelId).Distinct().Count());
    }

    private static SoakTally Soak(int runs, int sampleEvery, out long samples, int sampleTarget = 0)
    {
        var t = new SoakTally();
        long sampled = 0;
        Parallel.For(0, runs, new ParallelOptions { MaxDegreeOfParallelism = 8 }, (r, loop) =>
        {
            if (sampleTarget > 0 && Interlocked.Read(ref sampled) >= sampleTarget)
            {
                loop.Stop();
                return;
            }
            string ch = Characters[r % Characters.Length];
            int asc = (r / Characters.Length) % 2 == 0 ? 0 : 10;
            var policy = (Policy)((r / (Characters.Length * 2)) % 3);
            var sim = RunSim.NewRun(new RunConfig($"FPSOAK{r:D5}", ch, asc) { Content = ContentFilter.PortedOnly });
            var rng = new System.Random(r * 7919 + 17);
            var legal = new List<RunAction>(64);
            int cap = policy == Policy.BiasedBoosted ? 2500 : 3000;
            bool boosted = false;
            long steps = 0, fast = 0, prompts = 0;
            try
            {
                for (int n = 0; n < cap && !sim.IsTerminal; n++)
                {
                    if (policy == Policy.BiasedBoosted && !boosted && !sim.HasPendingChoice)
                    {
                        var p = sim.State.Players[0];
                        p.MaxHp = 999;
                        p.CurrentHp = 999;
                        boosted = true;
                    }
                    sim.LegalActions(legal);
                    var action = policy == Policy.Uniform ? legal[rng.Next(legal.Count)] : Biased(legal, rng);
                    string? blocker = sim.FastPathBlocker(action);
                    t.Blockers.AddOrUpdate(blocker ?? "(fast)", 1, (_, v) => v + 1);
                    steps++;
                    if (blocker == null)
                    {
                        fast++;
                    }
                    if (sampleEvery > 0 && blocker == null && n % sampleEvery == 0)
                    {
                        var tape = sim.Clone();
                        tape.Step(action);
                        sim.StepFastUnchecked(action);
                        if (tape.StateHash() != sim.StateHash())
                        {
                            t.Unsound.Enqueue($"{ch} A{asc} run {r} step {n} {action}: tape hash {tape.StateHash():x16} != clone-free {sim.StateHash():x16}");
                        }
                        if (tape.HasPendingChoice)
                        {
                            t.Unsound.Enqueue($"{ch} A{asc} run {r} step {n} {action}: tape prompted at {tape.PendingChoice!.SiteId}");
                        }
                        Interlocked.Increment(ref sampled);
                        continue;
                    }
                    sim.Step(action);
                    if (sim.PendingChoice is { } prompt)
                    {
                        prompts++;
                        t.Sources.AddOrUpdate(prompt.SourceId, 1, (_, v) => v + 1);
                        if (!PromptSites.IsKnownSource(prompt.SourceId))
                        {
                            t.Unknown.Enqueue($"{prompt.SiteId} (source {prompt.SourceId}) after {action} [{ch} A{asc} run {r} step {n}]");
                        }
                        if (blocker == null)
                        {
                            t.Unsound.Enqueue($"{prompt.SiteId} (source {prompt.SourceId}) after cleared {action} [{ch} A{asc} run {r} step {n}]");
                        }
                    }
                }
            }
            catch (FastPathPromptException ex)
            {
                t.Unsound.Enqueue($"{ch} A{asc} run {r}: {ex.Message}");
            }
            catch (NotImplementedException)
            {
                Interlocked.Increment(ref t.Gaps);
            }
            catch (Exception ex) when (ex is not AssertionException)
            {
                Interlocked.Increment(ref t.Errors);
                if (t.ErrorSamples.Count < 5)
                {
                    t.ErrorSamples.Enqueue($"{ch} A{asc} run {r}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            Interlocked.Add(ref t.Steps, steps);
            Interlocked.Add(ref t.FastSteps, fast);
            Interlocked.Add(ref t.Prompts, prompts);
            Interlocked.Increment(ref t.Runs);
        });
        samples = sampled;
        return t;
    }

    /// <summary>Play-biased policy over RunAction kinds (plays cards, claims rewards, buys, opens, travels).</summary>
    private static RunAction Biased(List<RunAction> legal, System.Random rng)
    {
        List<RunAction> Of(RunActionKind k) => legal.Where(a => a.Kind == k).ToList();
        RunAction Pick(List<RunAction> l) => l[rng.Next(l.Count)];
        var plays = Of(RunActionKind.PlayCard);
        if (legal.Any(a => a.Kind == RunActionKind.EndTurn))
        {
            if (plays.Count > 0 && rng.NextDouble() < 0.85) return Pick(plays);
            var pots = Of(RunActionKind.UsePotion);
            if (pots.Count > 0 && rng.NextDouble() < 0.15) return Pick(pots);
            return Pick(Of(RunActionKind.EndTurn));
        }
        foreach (var (kind, p) in new[] { (RunActionKind.ClaimReward, 0.9), (RunActionKind.PickRewardCard, 0.85), (RunActionKind.BuyEntry, 0.6),
                     (RunActionKind.OpenChest, 1.0), (RunActionKind.TakeTreasureRelic, 1.0), (RunActionKind.ChooseRestOption, 0.9),
                     (RunActionKind.ChooseEventOption, 1.0), (RunActionKind.Travel, 1.0) })
        {
            var l = Of(kind);
            if (l.Count > 0 && rng.NextDouble() < p) return Pick(l);
        }
        var notDiscard = legal.Where(a => a.Kind != RunActionKind.DiscardPotion).ToList();
        return Pick(notDiscard.Count > 0 ? notDiscard : legal);
    }

    private static void Report(string label, SoakTally t)
    {
        double frac = t.Steps == 0 ? 0 : (double)t.FastSteps / t.Steps;
        Console.WriteLine($"     FastPath {label}: {t.Runs} runs, {t.Steps} steps, fast {t.FastSteps} ({frac:P1}), {t.Prompts} prompts from {t.Sources.Count} sources, "
            + $"gaps {t.Gaps}, errors {t.Errors}");
        Console.WriteLine("     blockers: " + string.Join(", ", t.Blockers.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        Console.WriteLine("     sources: " + string.Join(", ", t.Sources.OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key} {kv.Value}")));
        foreach (var e in t.ErrorSamples)
        {
            Console.WriteLine("     error: " + e);
        }
    }
}
