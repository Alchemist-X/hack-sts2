using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Prompt macros v2 (PromptMacros, config <c>macros</c> ∋ <c>prompt</c> / <c>all</c>, W4 2026-11-04): no PromptCancel row
/// at any prompt; a pick that leaves Confirm as the only way on confirms in the same step; the PromptConfirm row is the
/// explicit DONE / DECLINE; only the game's selectable options are listed; the rest / shop macro rows fall back to the
/// plain open row past 64 candidates. Each macro step equals the game's manual click sequence (StateHash).
/// </summary>
public static unsafe partial class VecTests
{
    private const RoomMacroSet AllMacros = RoomMacroSet.All;

    /// <summary>
    /// A fresh Ironclad run driven (first legal action) into its first combat decision, then the hand replaced by
    /// <paramref name="hand"/> (fresh instances; the old hand goes to the draw pile).
    /// </summary>
    private static RunSim CombatHandStart(string seed, params string[] hand)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var p = sim.State.Players[0];
        p.MaxHp = 999;
        p.CurrentHp = 999;
        for (int step = 0; step < 500; step++)
        {
            if (!sim.HasPendingChoice && sim.State.Combat is { IsInProgress: true } && sim.LegalActions().Contains(RunAction.Of(RunActionKind.EndTurn)))
            {
                break;
            }
            sim.Step(sim.LegalActions()[0]);
        }
        var cs = sim.State.Combat;
        Assert.True(cs is { IsInProgress: true } && !sim.HasPendingChoice, "reached a combat decision");
        var player = cs!.Player;
        while (player.Hand.Count > 0)
        {
            player.DrawPile.AddInternal(player.Hand.RemoveAtInternal(player.Hand.Count - 1));
        }
        foreach (var id in hand)
        {
            player.Hand.AddInternal(cs.CreateCard(id, player));
        }
        return sim;
    }

    /// <summary>The PlayCard candidate of the first hand card with id <paramref name="cardId"/>.</summary>
    private static RunAction PlayOfCard(RunSim sim, string cardId)
    {
        var hand = sim.State.Combat!.Player.Hand;
        int h = -1;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Id == cardId)
            {
                h = i;
                break;
            }
        }
        Assert.True(h >= 0, $"{cardId} in hand");
        var legal = sim.LegalActions().ToList();
        int k = legal.FindIndex(a => a.Kind == RunActionKind.PlayCard && a.A == h);
        Assert.True(k >= 0, $"{cardId} is playable");
        return legal[k];
    }

    /// <summary>A one-slot env restored to a clone of <paramref name="sim"/>.</summary>
    private static VecEnv EnvAt(RunSim sim, RoomMacroSet set, NativeBuffers nb)
    {
        var env = MacroEnv(set);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        return env;
    }

    private static void StepRow(VecEnv env, NativeBuffers nb, RunAction a, string what)
    {
        var cands = CandList(nb);
        int r = cands.IndexOf(a);
        Assert.True(r >= 0, $"{what}: {a} is a candidate ({string.Join(", ", cands)})");
        env.Step(new[] { r }, nb.Table);
        Assert.Equal(0, nb.Slot(0).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap | SlotStatus.IllegalAction),
            $"{what}: clean step ({env.Slots[0].LastError})");
    }

    private static void AssertNoCancel(NativeBuffers nb, string what)
        => Assert.False(CandList(nb).Any(a => a.Kind == RunActionKind.PromptCancel), $"{what}: no PromptCancel row");

    public static void Test_VecConfig_PromptMacrosParse()
    {
        Assert.Equal(RoomMacroSet.All, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": \"all\"}").Macros);
        Assert.Equal(RoomMacroSet.All, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"rest\", \"shop\", \"prompt\"]}").Macros);
        Assert.Equal(RoomMacroSet.Prompt, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"PROMPT\"]}").Macros);
        Assert.Equal(RoomMacroSet.All, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": \"rest,all\"}").Macros);
        Assert.SequenceEqual(new[] { "rest", "shop", "prompt" }, RoomMacros.NamesOf(RoomMacroSet.All));
        Assert.Throws<ArgumentException>(() => VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"cancel\"]}"));
        Assert.Equal(512, (int)SlotStatus.MacroFallback, "appended status bit");
    }

    /// <summary>
    /// HAND_TRICK: "choose a skill in hand" — the prompt's options are exactly the hand's skills (the game's filter); the
    /// Vec rows are one PromptSelect per such option (no attack, no cancel); one step = the game's pick (same StateHash).
    /// </summary>
    public static void Test_VecCand_PromptMacroHandSelectSkillsOnly()
    {
        var sim = CombatHandStart("PMAC-HANDTRICK", HandTrick.CardId, DefendIronclad.CardId, StrikeIronclad.CardId, DefendIronclad.CardId,
            StrikeIronclad.CardId, "BASH");
        var play = PlayOfCard(sim, HandTrick.CardId);
        using var nb = new NativeBuffers(1);
        var env = EnvAt(sim, AllMacros, nb);
        StepRow(env, nb, play, "play HAND_TRICK");
        var esim = env.Slots[0].Sim!;
        var prompt = Assert.NotNull(esim.PendingChoice, "HAND_TRICK opens its hand prompt");
        Assert.Equal(ChoicePromptKind.HandSelect, prompt.Kind);
        var hand = esim.State.Combat!.Player.Hand;
        var skills = hand.Cards.Where(c => c.Type == CardType.Skill).Select(c => c.InstanceId).ToHashSet();
        Assert.True(hand.Cards.Any(c => c.Type != CardType.Skill), "the hand holds non-skills");
        Assert.Equal(2, prompt.Options.Count, "two Defends selectable");
        Assert.True(prompt.Options.All(o => skills.Contains(o.InstanceId)), "the game's filter: skills only");
        var cands = CandList(nb);
        Assert.SequenceEqual(Enumerable.Range(0, prompt.Options.Count).Select(j => RunAction.Of(RunActionKind.PromptSelect, j)), cands,
            "one row per selectable option, nothing else");
        for (int r = 0; r < cands.Count; r++)
        {
            Assert.Equal(Vocab.Card(DefendIronclad.CardId), CandRow(nb, 0, r)[CandCol.CardId], "each row is a Defend");
        }

        var manual = sim.Clone();
        manual.Step(play);
        manual.Step(RunAction.Of(RunActionKind.PromptSelect, 1));
        StepRow(env, nb, RunAction.Of(RunActionKind.PromptSelect, 1), "pick the second Defend");
        Assert.False(env.Slots[0].Sim!.HasPendingChoice);
        Assert.Equal(manual.StateHash(), env.StateHash(0), "macro = the game's pick");
    }

    /// <summary>
    /// PURITY: exhaust up to 3 (MinSelect 0, manual confirm). Rows: the picks plus the explicit DONE (PromptConfirm), no
    /// cancel; DONE after two picks and the auto-confirm at the third pick each equal the manual click sequence.
    /// </summary>
    public static void Test_VecCand_PromptMacroUpToNHasDoneNoCancel()
    {
        var sim = CombatHandStart("PMAC-PURITY", Purity.CardId, StrikeIronclad.CardId, DefendIronclad.CardId, StrikeIronclad.CardId,
            DefendIronclad.CardId, "BASH");
        var play = PlayOfCard(sim, Purity.CardId);
        var confirm = RunAction.Of(RunActionKind.PromptConfirm);
        RunAction Pick(int j) => RunAction.Of(RunActionKind.PromptSelect, j);

        // DONE after two picks
        using (var nb = new NativeBuffers(1))
        {
            var env = EnvAt(sim, AllMacros, nb);
            StepRow(env, nb, play, "play PURITY");
            var prompt = Assert.NotNull(env.Slots[0].Sim!.PendingChoice);
            Assert.Equal(0, prompt.MinSelect);
            Assert.Equal(3, prompt.MaxSelect);
            Assert.True(CandList(nb).Contains(confirm), "DECLINE (confirm with no pick) offered: the game allows 0");
            AssertNoCancel(nb, "PURITY");
            StepRow(env, nb, Pick(0), "pick 1");
            Assert.True(env.Slots[0].Sim!.HasPendingChoice, "up to 3: still open after one pick");
            Assert.True(CandList(nb).Contains(confirm), "DONE offered");
            StepRow(env, nb, Pick(2), "pick 2");
            AssertNoCancel(nb, "PURITY after 2 picks");
            StepRow(env, nb, confirm, "DONE");
            var manual = sim.Clone();
            foreach (var a in new[] { play, Pick(0), Pick(2), confirm })
            {
                manual.Step(a);
            }
            Assert.False(env.Slots[0].Sim!.HasPendingChoice);
            Assert.Equal(manual.StateHash(), env.StateHash(0), "picks + DONE = the game's clicks");
        }

        // the third pick completes the selection: it confirms in the same step
        using (var nb = new NativeBuffers(1))
        {
            var env = EnvAt(sim, AllMacros, nb);
            StepRow(env, nb, play, "play PURITY");
            StepRow(env, nb, Pick(1), "pick 1");
            StepRow(env, nb, Pick(3), "pick 2");
            StepRow(env, nb, Pick(0), "pick 3");
            Assert.False(env.Slots[0].Sim!.HasPendingChoice, "the 3rd pick commits");
            var manual = sim.Clone();
            foreach (var a in new[] { play, Pick(1), Pick(3), Pick(0), confirm })
            {
                manual.Step(a);
            }
            Assert.Equal(manual.StateHash(), env.StateHash(0), "3 picks (auto-confirm) = picks + Confirm");
            Assert.Equal(4L, nb.Slot(0).Episode[EpisodeCol.StepIndex], "4 decisions: play + 3 picks");
        }

        // macros off: the raw prompt (picks + confirm), as before
        using (var nb = new NativeBuffers(1))
        {
            var env = EnvAt(sim, RoomMacroSet.None, nb);
            StepRow(env, nb, play, "play PURITY (off)");
            var raw = env.Slots[0].Sim!.LegalActions();
            Assert.SequenceEqual(raw, CandList(nb), "macros off: the game's prompt actions");
        }
    }

    /// <summary>HIDDEN_DAGGERS: discard exactly 2 — two picks, the second completes the prompt; no confirm / cancel rows.</summary>
    public static void Test_VecCand_PromptMacroExactlyKCommitsAtK()
    {
        var sim = CombatHandStart("PMAC-DAGGERS", HiddenDaggers.CardId, StrikeIronclad.CardId, DefendIronclad.CardId, StrikeIronclad.CardId,
            DefendIronclad.CardId);
        var play = PlayOfCard(sim, HiddenDaggers.CardId);
        using var nb = new NativeBuffers(1);
        var env = EnvAt(sim, AllMacros, nb);
        StepRow(env, nb, play, "play HIDDEN_DAGGERS");
        var prompt = Assert.NotNull(env.Slots[0].Sim!.PendingChoice);
        Assert.Equal(2, prompt.MinSelect);
        Assert.Equal(2, prompt.MaxSelect);
        Assert.True(CandList(nb).All(a => a.Kind == RunActionKind.PromptSelect), "exactly k: picks only");
        StepRow(env, nb, RunAction.Of(RunActionKind.PromptSelect, 3), "pick 1");
        Assert.True(CandList(nb).All(a => a.Kind == RunActionKind.PromptSelect), "no way back after the first pick");
        StepRow(env, nb, RunAction.Of(RunActionKind.PromptSelect, 0), "pick 2");
        Assert.False(env.Slots[0].Sim!.HasPendingChoice);
        var manual = sim.Clone();
        manual.Step(play);
        manual.Step(RunAction.Of(RunActionKind.PromptSelect, 3));
        manual.Step(RunAction.Of(RunActionKind.PromptSelect, 0));
        Assert.Equal(manual.StateHash(), env.StateHash(0), "k picks = the game's k picks");
        Assert.True(CandList(nb).Contains(RunAction.Of(RunActionKind.EndTurn)), "back at the combat decision");
    }

    /// <summary>
    /// COOK (remove 2, cancelable, manual confirm): with prompt macros the open row stays, the prompt has no cancel row,
    /// the second pick confirms; equal to COOK, pick, pick, Confirm. Without macros the raw prompt has its cancel.
    /// </summary>
    public static void Test_VecCand_PromptMacroCookNoCancel()
    {
        var sim = RestStart("PMAC-COOK");
        var room = (RestSiteRoom)RoomStack.CurrentRoom(sim.State)!;
        room.Options.Add(new CookRestSiteOption(sim.State.Players[0]));
        int cook = room.Options.Count - 1;
        var open = RunAction.Of(RunActionKind.ChooseRestOption, cook);
        Assert.True(sim.LegalActions().Contains(open), "COOK offered");

        using var nbOff = new NativeBuffers(1);
        var off = EnvAt(sim, RoomMacroSet.None, nbOff);
        StepRow(off, nbOff, open, "COOK (off)");
        Assert.True(CandList(nbOff).Contains(RunAction.Of(RunActionKind.PromptCancel)), "the raw COOK prompt is cancelable");

        using var nb = new NativeBuffers(1);
        var env = EnvAt(sim, AllMacros, nb);
        StepRow(env, nb, open, "COOK");
        var prompt = Assert.NotNull(env.Slots[0].Sim!.PendingChoice);
        Assert.Equal(ChoicePromptKind.DeckRemove, prompt.Kind);
        Assert.True(prompt.Cancelable && prompt.RequireConfirm);
        AssertNoCancel(nb, "COOK");
        Assert.True(CandList(nb).All(a => a.Kind == RunActionKind.PromptSelect), "picks only (exactly 2)");
        var first = CandList(nb)[0];
        StepRow(env, nb, first, "pick 1");
        AssertNoCancel(nb, "COOK after a pick");
        var second = CandList(nb)[0];
        StepRow(env, nb, second, "pick 2");
        Assert.False(env.Slots[0].Sim!.HasPendingChoice, "the 2nd pick confirms");
        var manual = sim.Clone();
        foreach (var a in new[] { open, first, second, RunAction.Of(RunActionKind.PromptConfirm) })
        {
            manual.Step(a);
        }
        Assert.Equal(manual.StateHash(), env.StateHash(0), "COOK, pick, pick (auto-confirm) = COOK, pick, pick, Confirm");
        Assert.Equal(sim.State.Players[0].Deck.Count - 2, env.Slots[0].Sim!.State.Players[0].Deck.Count, "two cards removed");
    }

    /// <summary>
    /// The 64-row cap: a deck with 80 upgradable cards. rest,shop (W3) still expands SMITH into 80+ rows; all falls back to
    /// the plain SMITH row (status MACRO_FALLBACK), whose prompt has no cancel and confirms at the pick.
    /// </summary>
    public static void Test_VecCand_PromptMacroRestFallbackPastMaxRows()
    {
        var sim = RestStart("PMAC-BIGDECK");
        var rs = sim.State;
        var p = rs.Players[0];
        for (int i = 0; i < 80; i++)
        {
            var card = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), rs.AllocateCardInstanceId());
            card.AttachTo(p);
            p.Deck.Add(card);
        }
        var room = (RestSiteRoom)RoomStack.CurrentRoom(rs)!;
        int smith = room.Options.FindIndex(o => o is SmithRestSiteOption);
        var plain = RunAction.Of(RunActionKind.ChooseRestOption, smith);
        int upgradable = p.Deck.Count(c => c.IsUpgradable);
        Assert.True(upgradable > PromptMacros.MaxRows);

        var w3 = PromptMacros.Candidates(sim, BothMacros);
        Assert.SequenceEqual(RoomMacros.Candidates(sim, BothMacros), w3, "rest,shop: the W3 lists");
        Assert.True(w3.Count > PromptMacros.MaxRows && !w3.Contains(plain), "rest,shop: no cap");
        var all = PromptMacros.Candidates(sim, AllMacros);
        Assert.SequenceEqual(RewardMacros.Candidates(sim), all, "all: the plain rest-site list");

        using var nb = new NativeBuffers(1);
        var env = EnvAt(sim, AllMacros, nb);
        Assert.True((nb.Slot(0).Status & (int)SlotStatus.MacroFallback) != 0, "MACRO_FALLBACK on the observation");
        using var nbW3 = new NativeBuffers(1);
        EnvAt(sim, BothMacros, nbW3);
        Assert.Equal(0, nbW3.Slot(0).Status & (int)SlotStatus.MacroFallback, "never with rest,shop");
        StepRow(env, nb, plain, "SMITH");
        AssertNoCancel(nb, "SMITH prompt");
        Assert.True(CandList(nb).All(a => a.Kind == RunActionKind.PromptSelect));
        StepRow(env, nb, RunAction.Of(RunActionKind.PromptSelect, 5), "pick");
        Assert.False(env.Slots[0].Sim!.HasPendingChoice, "the pick confirms");
        var manual = sim.Clone();
        foreach (var a in new[] { plain, RunAction.Of(RunActionKind.PromptSelect, 5), RunAction.Of(RunActionKind.PromptConfirm) })
        {
            manual.Step(a);
        }
        Assert.Equal(manual.StateHash(), env.StateHash(0), "SMITH, pick (auto-confirm) = SMITH, pick, Confirm");
    }

    /// <summary>
    /// 32 play-biased runs (every character) with all macros for 3000 steps: no step error, no PromptCancel row ever, no
    /// slot ever at the SMITH / merchant-removal prompt unless that observation fell back, and prompt picks taken.
    /// </summary>
    public static void Test_VecEnv_PromptMacrosSoak()
    {
        const int N = 32;
        var env = MacroEnv(AllMacros, N, poolCount: 64, threads: 4);
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 91);
        var act = new int[N];
        int picks = 0, prompts = 0;
        for (int t = 0; t < 3000; t++)
        {
            for (int i = 0; i < N; i++)
            {
                var cands = CandList(nb, i);
                Assert.False(cands.Any(a => a.Kind == RunActionKind.PromptCancel), $"slot {i} step {t}: a cancel row");
                if (env.Slots[i].Sim?.HasPendingChoice == true)
                {
                    prompts++;
                }
                act[i] = PlayBiased(nb, i, ref rng[i]);
                if (act[i] >= 0 && cands[act[i]].Kind == RunActionKind.PromptSelect)
                {
                    picks++;
                }
            }
            env.Step(act, nb.Table);
            for (int i = 0; i < N; i++)
            {
                Assert.Equal(0, nb.Slot(i).Status & (int)SlotStatus.SimError, $"slot {i} step {t}: {env.Slots[i].LastError}");
            }
        }
        Assert.True(prompts > 0 && picks > 0, $"prompts seen ({prompts}) and picked ({picks})");
    }
}
