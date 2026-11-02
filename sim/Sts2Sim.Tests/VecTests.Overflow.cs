using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// CONTRACTS.md §D3 / §D7 T3 overflow paths: prompt_options holds CAP_PROMPT_OPTIONS (128) rows and sets TABLE_OVERFLOW
/// beyond that; cand holds CAP_CANDIDATES (192) rows and sets CANDIDATE_OVERFLOW; only indices below cand_n are
/// selectable (E2c ticket 2).
/// </summary>
public static unsafe partial class VecTests
{
    private static readonly int PromptItemCol = Schema.Col(Schema.PromptOptionFields, "item_id");
    private static readonly int PromptUpgradeCol = Schema.Col(Schema.PromptOptionFields, "upgrade");
    private static readonly int PromptPickedCol = Schema.Col(Schema.PromptOptionFields, "picked");
    private static readonly int PromptKindCol = Schema.Col(Schema.PromptOptionFields, "kind_of_item");
    private static readonly int PromptOrdinalCol = Schema.Col(Schema.PromptOptionFields, "ordinal");

    /// <summary>
    /// A combat PileSelect (WISH) over a draw pile stuffed past CAP_PROMPT_OPTIONS while the deck and every other table stay
    /// within their caps, so TABLE_OVERFLOW can only come from prompt_options: exactly 128 rows are written, prompt_option_n
    /// keeps the true count, and the options beyond the table stay selectable through cand (with their card_id).
    /// </summary>
    public static void Test_VecEnv_PromptTableOverflow()
    {
        var sim = FirstCombat("VEC-PROMPT-OVF");
        var cs = sim.State.Combat!;
        var cp = cs.Player;
        var strike = CardRegistry.Get(StrikeIronclad.CardId);
        while (cp.DrawPile.Count < 150)
        {
            cp.DrawPile.AddInternal(cs.CreateCard(strike, cp));
        }
        var wish = cs.CreateCard(CardRegistry.Get(Wish.CardId), cp);
        cp.Hand.AddInternal(wish);
        Assert.True(sim.State.Players[0].Deck.Count <= Caps.Deck && cp.Hand.Count <= Caps.Hand, "only the prompt can overflow");

        using var nb = new NativeBuffers(1);
        var control = SlotStatus.Ok;
        ObservationEncoder.Encode(sim, nb.Slot(0), ref control);
        Assert.False(control.HasFlag(SlotStatus.TableOverflow), "control: the stuffed combat state overflows no table");

        var play = sim.LegalActions().First(a => a.Kind == RunActionKind.PlayCard && ReferenceEquals(cp.Hand[a.A], wish));
        sim.Step(play);
        var prompt = Assert.NotNull(sim.PendingChoice);
        var legal = sim.LegalActions().ToList();
        int n = prompt.Options.Count;
        Assert.True(n > Caps.PromptOptions && legal.Count == n && n < Caps.Candidates, $"{n} options, {legal.Count} legal actions");

        var env = NewEnv(1, 1, 0);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        var s = nb.Slot(0);
        var status = (SlotStatus)s.Status;
        Assert.True(status.HasFlag(SlotStatus.TableOverflow), $"prompt_options overflow sets TABLE_OVERFLOW (status {status})");
        Assert.False(status.HasFlag(SlotStatus.CandidateOverflow), $"{n} candidates fit (status {status})");
        Assert.Equal((int)RunPhase.Prompt, s.GlobalI[GI.DecisionKind]);
        Assert.Equal(n, s.GlobalI[GI.PromptOptionN], "prompt_option_n is the true count");
        Assert.True(s.GlobalI[GI.DeckN] <= Caps.Deck);
        AssertPromptRows(s, prompt, Caps.PromptOptions);
        Assert.Equal(n, s.CandN, "every option is a candidate");
        for (int r = 0; r < n; r++)
        {
            var row = CandRow(nb, 0, r);
            Assert.Equal(legal[r], CandidateTable.ToAction(row), $"cand row {r}");
            Assert.Equal(legal[r].A, row[CandCol.OptionIndex]);
            Assert.Equal(Vocab.Card(prompt.Options[legal[r].A].ModelId), row[CandCol.CardId], $"cand row {r} card_id (also past the prompt table)");
        }

        // an option past the prompt table is a selectable candidate and resolves like the RunAction
        int beyond = legal.FindIndex(a => a.A >= Caps.PromptOptions);
        Assert.True(beyond >= 0);
        var expect = sim.Clone();
        expect.Step(legal[beyond]);
        env.Step(new[] { beyond }, nb.Table);
        Assert.Equal(0, s.Status & (int)(SlotStatus.IllegalAction | SlotStatus.SimError | SlotStatus.ContentGap), $"status {s.Status}");
        Assert.Equal(expect.StateHash(), env.StateHash(0), "candidate index past the prompt table ≡ RunAction");
        Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.TableOverflow), "the prompt is answered: no table overflows any more");
    }

    /// <summary>
    /// The merchant card-removal prompt (DeckRemove) over 140 and 200 deck cards. 140: 128 prompt rows + TABLE_OVERFLOW,
    /// 141 candidates (140 picks + cancel). 200: 201 legal actions → cand_n = 192 + CANDIDATE_OVERFLOW; indices at or after
    /// cand_n — including 200, the cancel that LegalActions lists past the cap — are ILLEGAL_ACTION with the slot unchanged;
    /// an index below the cap steps like its RunAction and the prompt resolves normally.
    /// </summary>
    public static void Test_VecEnv_RemovalPromptOverflow()
    {
        var env = NewEnv(1, 1, 0);
        using var nb = new NativeBuffers(1);
        using var before = new NativeBuffers(1);
        var s = nb.Slot(0);

        // 140 cards: only the prompt table overflows past its cap (the deck table does too: 140 > 96)
        var sim140 = RemovalPrompt(140, "VEC-REMOVE-140");
        var legal140 = sim140.LegalActions().ToList();
        Assert.Equal(141, legal140.Count, "140 picks + cancel");
        env.Restore(0, env.Pool.AddSnapshot(sim140.Clone()), nb.Table);
        var st = (SlotStatus)s.Status;
        Assert.True(st.HasFlag(SlotStatus.TableOverflow) && !st.HasFlag(SlotStatus.CandidateOverflow), $"status {st}");
        Assert.Equal(140, s.GlobalI[GI.PromptOptionN]);
        Assert.Equal(140, s.GlobalI[GI.DeckN]);
        Assert.Equal((int)ChoicePromptKind.DeckRemove, s.GlobalI[GI.PromptKind]);
        Assert.True(s.GlobalI[GI.ShopEntryN] > 0, "the removal prompt keeps the shop as context");
        AssertPromptRows(s, sim140.PendingChoice!, Caps.PromptOptions);
        Assert.Equal(141, s.CandN);
        Assert.Equal((int)RunActionKind.PromptCancel, CandRow(nb, 0, 140)[CandCol.Kind], "cancel is the last candidate");
        // pick option 135 (past the prompt table), then confirm through the candidate table
        var expect = sim140.Clone();
        expect.Step(legal140[135]);
        env.Step(new[] { 135 }, nb.Table);
        Assert.Equal(expect.StateHash(), env.StateHash(0));
        Assert.Equal(1, s.GlobalI[GI.PromptPicked]);
        var afterPick = expect.LegalActions().ToList();
        int confirm = afterPick.IndexOf(RunAction.Of(RunActionKind.PromptConfirm));
        Assert.True(confirm >= 0 && s.CandN == afterPick.Count, "confirm is listed after the pick");
        expect.Step(afterPick[confirm]);
        env.Step(new[] { confirm }, nb.Table);
        Assert.Equal(expect.StateHash(), env.StateHash(0));
        Assert.Equal(139, s.GlobalI[GI.DeckN], "the removal went through");
        Assert.Equal((int)RunPhase.Shop, s.GlobalI[GI.DecisionKind]);

        // 200 cards: the candidate list overflows
        var sim200 = RemovalPrompt(200, "VEC-REMOVE-200");
        var legal = sim200.LegalActions().ToList();
        Assert.Equal(201, legal.Count, "200 picks + cancel");
        Assert.Equal(RunActionKind.PromptCancel, legal[200].Kind);
        env.Restore(0, env.Pool.AddSnapshot(sim200.Clone()), nb.Table);
        st = (SlotStatus)s.Status;
        Assert.True(st.HasFlag(SlotStatus.CandidateOverflow) && st.HasFlag(SlotStatus.TableOverflow), $"status {st}");
        Assert.Equal(Caps.Candidates, s.CandN);
        Assert.Equal(200, s.GlobalI[GI.PromptOptionN]);
        AssertPromptRows(s, sim200.PendingChoice!, Caps.PromptOptions);
        var prompt = sim200.PendingChoice!;
        for (int r = 0; r < Caps.Candidates; r++)
        {
            var row = CandRow(nb, 0, r);
            Assert.Equal(legal[r], CandidateTable.ToAction(row), $"cand row {r}");
            Assert.Equal(Vocab.Card(prompt.Options[legal[r].A].ModelId), row[CandCol.CardId], $"cand row {r} card_id");
        }

        // indices at or after cand_n are illegal (200 = the cancel LegalActions lists past the cap); nothing changes
        ulong hash = env.StateHash(0);
        env.Step(new[] { -1 }, nb.Table);
        for (int k = 0; k < Buf.Count; k++)
        {
            nb.AllBytes(k).CopyTo(before.AllBytes(k));
        }
        foreach (int bad in new[] { Caps.Candidates, Caps.Candidates + 1, 200, int.MaxValue })
        {
            env.Step(new[] { bad }, nb.Table);
            st = (SlotStatus)s.Status;
            Assert.True(st.HasFlag(SlotStatus.IllegalAction) && !st.HasFlag(SlotStatus.Reset), $"index {bad}: status {st}");
            Assert.True(st.HasFlag(SlotStatus.CandidateOverflow), $"index {bad}: the rewritten observation still overflows");
            Assert.Equal(hash, env.StateHash(0), $"index {bad} leaves the slot unchanged");
            AssertBuffersEqual(before, nb, $"index {bad}: same observation", skip: Buf.Status);
        }

        // the last selectable index (191) steps like its RunAction; the pick leaves [confirm, cancel]
        expect = sim200.Clone();
        expect.Step(legal[Caps.Candidates - 1]);
        env.Step(new[] { Caps.Candidates - 1 }, nb.Table);
        st = (SlotStatus)s.Status;
        Assert.Equal(0, (int)(st & (SlotStatus.IllegalAction | SlotStatus.SimError | SlotStatus.ContentGap)), $"status {st}");
        Assert.Equal(expect.StateHash(), env.StateHash(0), "candidate 191 ≡ RunAction");
        Assert.False(st.HasFlag(SlotStatus.CandidateOverflow), "after the pick only confirm and cancel remain");
        Assert.Equal(2, s.CandN);
        Assert.Equal(1, s.GlobalI[GI.PromptPicked]);
        env.Step(new[] { 0 }, nb.Table);
        Assert.Equal(199, s.GlobalI[GI.DeckN], "confirmed: one card removed");
        Assert.Equal((int)RunPhase.Shop, s.GlobalI[GI.Phase]);
    }

    /// <summary>
    /// Row r &lt; <paramref name="rows"/> of prompt_options describes option r (ordinal, card id, upgrade, kind "card",
    /// not picked); rows at or beyond it are zero. Exactly min(options, CAP) rows are written.
    /// </summary>
    private static void AssertPromptRows(SlotBuffers s, ChoicePrompt prompt, int rows)
    {
        Assert.Equal(Math.Min(prompt.Options.Count, Caps.PromptOptions), rows);
        int w = Schema.PromptOptionFields.Length;
        var table = s.PromptOptions;
        int card = Vocab.PromptItemKind("card");
        int written = 0;
        for (int r = 0; r < Caps.PromptOptions; r++)
        {
            var row = table.Slice(r * w, w);
            if (r >= rows)
            {
                Assert.True(row.ToArray().All(v => v == 0), $"prompt row {r} is zero");
                continue;
            }
            var option = prompt.Options[r];
            Assert.Equal(r, row[PromptOrdinalCol], $"prompt row {r} ordinal");
            Assert.Equal(Vocab.Card(option.ModelId), row[PromptItemCol], $"prompt row {r} item_id");
            Assert.True(row[PromptItemCol] >= 2, $"prompt row {r} holds a real card id");
            Assert.Equal(option.UpgradeLevel, row[PromptUpgradeCol]);
            Assert.Equal(card, row[PromptKindCol]);
            Assert.Equal(0, row[PromptPickedCol]);
            written++;
        }
        Assert.Equal(rows, written, "rows written");
    }

    /// <summary>A fresh Ironclad run stepped with its first legal action until the first combat decision point.</summary>
    private static RunSim FirstCombat(string seed)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        for (int step = 0; step < 500 && !sim.IsTerminal; step++)
        {
            if (sim.Phase == RunPhase.Combat && !sim.HasPendingChoice && sim.State.Combat is { IsInProgress: true })
            {
                return sim;
            }
            sim.Step(sim.LegalActions()[0]);
        }
        Assert.Fail("no combat decision point reached");
        return sim;
    }

    /// <summary>
    /// A fresh Ironclad run with its deck padded to <paramref name="deckSize"/> cards and 999 gold, put into a merchant room
    /// (entered directly), that buys the card-removal service: the pending DeckRemove prompt lists every deck card.
    /// </summary>
    private static RunSim RemovalPrompt(int deckSize, string seed)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var rs = sim.State;
        var p = rs.Players[0];
        int start = p.Deck.Count;
        for (int i = 0; p.Deck.Count < deckSize; i++)
        {
            p.Deck.Add(CardFactory.CreateRunCard(rs, p.Deck[i % start].Def, p));
        }
        p.Gold = 999;
        RoomStack.EnterRoom(rs, new MerchantRoom());
        sim.Loop.MapOpen = false;
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Shop, sim.Phase);
        var entries = ((MerchantRoom)RoomStack.CurrentRoom(rs)!).Inventory!.AllEntries;
        Assert.True(entries[^1] is MerchantCardRemovalEntry, "the removal service is the last shop entry");
        sim.Step(RunAction.Of(RunActionKind.BuyEntry, entries.Count - 1));
        var prompt = Assert.NotNull(sim.PendingChoice);
        Assert.Equal(ChoicePromptKind.DeckRemove, prompt.Kind);
        Assert.Equal(deckSize, prompt.Options.Count);
        return sim;
    }
}
