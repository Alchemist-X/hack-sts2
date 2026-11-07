using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Rest-site / shop room macros (RoomMacros, config <c>macros</c>, W3 2026-11-04): with <c>rest</c> the SMITH row of a rest
/// site becomes one ChooseRestOption(A, B = deck card) row per upgradable deck card, with <c>shop</c> the card-removal
/// BuyEntry becomes one BuyEntry(A, B = deck card) row per removable card; each runs open → PromptSelect → PromptConfirm
/// in one step (no cancel path). Off (default) every candidate list and buffer is what it was.
/// </summary>
public static unsafe partial class VecTests
{
    private const RoomMacroSet BothMacros = RoomMacroSet.Rest | RoomMacroSet.Shop;

    /// <summary>A fresh Ironclad run (opening prompt cleared) at <paramref name="hp"/> HP, put straight into a rest site.</summary>
    private static RunSim RestStart(string seed, int hp = 40)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var rs = sim.State;
        rs.Players[0].CurrentHp = hp;
        RoomStack.EnterRoom(rs, new RestSiteRoom());
        sim.Loop.MapOpen = false;
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.RestSite, sim.Phase);
        return sim;
    }

    private static VecEnv MacroEnv(RoomMacroSet set, int n = 1, int poolCount = 0, int threads = 1)
        => new(new VecConfig(n, Threads: threads, Macros: set,
            Pool: poolCount > 0 ? new FreshPoolSpec(AllCharacters, Count: poolCount, SeedPrefix: "VTMACRO") : null));

    /// <summary>The game's own clicks for a macro on a clone: open, PromptSelect of the deck card's option, PromptConfirm.</summary>
    private static RunSim ManualMacro(RunSim sim, RunActionKind kind, int a, int deckIndex)
    {
        var m = sim.Clone();
        int instance = m.State.Players[0].Deck[deckIndex].InstanceId;
        m.Step(RunAction.Of(kind, a));
        var prompt = m.PendingChoice;
        Assert.True(prompt != null, "the open click shows a prompt");
        int j = prompt!.Options.ToList().FindIndex(o => o.InstanceId == instance);
        Assert.True(j >= 0, "the deck card is an option");
        m.Step(RunAction.Of(RunActionKind.PromptSelect, j));
        Assert.True(m.LegalActions().Contains(RunAction.Of(RunActionKind.PromptCancel)), "the raw prompt has a cancel path");
        m.Step(RunAction.Of(RunActionKind.PromptConfirm));
        Assert.False(m.HasPendingChoice);
        return m;
    }

    private static int RowOf(List<RunAction> cands, RunAction a)
    {
        int r = cands.IndexOf(a);
        Assert.True(r >= 0, $"{a} is a candidate");
        return r;
    }

    /// <summary>Every buffer of slot 0 except cand / cand_n is byte-identical between the two envs' observations.</summary>
    private static void AssertSameObservation(NativeBuffers x, NativeBuffers y, string what)
    {
        for (int k = 0; k < Buf.Count; k++)
        {
            if (k is Buf.Cand or Buf.CandN || !Buf.Observation.Contains(k))
            {
                continue;
            }
            Assert.True(x.AllBytes(k).SequenceEqual(y.AllBytes(k)), $"{what}: buffer {Schema.Buffers[k].Name} differs");
        }
    }

    public static void Test_VecConfig_MacrosParse()
    {
        Assert.Equal(RoomMacroSet.None, new VecConfig(1).Macros, "default off");
        Assert.Equal(RoomMacroSet.None, VecConfig.FromJson("{\"num_envs\": 1}").Macros, "config without the key: off");
        Assert.Equal(BothMacros, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"rest\", \"shop\"]}").Macros);
        Assert.Equal(RoomMacroSet.Rest, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"REST\"]}").Macros);
        Assert.Equal(BothMacros, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": \"rest,shop\"}").Macros);
        Assert.Equal(RoomMacroSet.Shop, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": \"shop\"}").Macros);
        Assert.Equal(RoomMacroSet.None, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": []}").Macros);
        Assert.Equal(RoomMacroSet.None, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": \"\"}").Macros);
        Assert.Equal(RoomMacroSet.None, VecConfig.FromJson("{\"num_envs\": 1, \"macros\": null}").Macros);
        Assert.Throws<ArgumentException>(() => VecConfig.FromJson("{\"num_envs\": 1, \"macros\": [\"event\"]}"));
        Assert.Throws<ArgumentException>(() => VecConfig.FromJson("{\"num_envs\": 1, \"macros\": 3}"));
        Assert.SequenceEqual(new[] { "rest", "shop" }, RoomMacros.NamesOf(BothMacros));
    }

    public static void Test_VecCand_RoomMacroSmithUpgradesOneCard()
    {
        var sim = RestStart("MACRO-REST");
        var room = (RestSiteRoom)RoomStack.CurrentRoom(sim.State)!;
        int smith = room.Options.FindIndex(o => o is SmithRestSiteOption);
        int heal = room.Options.FindIndex(o => o is HealRestSiteOption);
        Assert.True(smith >= 0 && heal >= 0, "HEAL and SMITH offered");
        var deck = sim.State.Players[0].Deck;
        int bash = deck.FindIndex(c => c.Id == "BASH");
        Assert.True(bash >= 0, "Ironclad starts with Bash");

        var raw = RewardMacros.Candidates(sim);
        Assert.True(raw.Contains(RunAction.Of(RunActionKind.ChooseRestOption, smith)), "the raw SMITH row");
        Assert.SequenceEqual(raw, RoomMacros.Candidates(sim, RoomMacroSet.None), "macros off: unchanged");
        Assert.SequenceEqual(raw, RoomMacros.Candidates(sim, RoomMacroSet.Shop), "shop macros leave the rest site alone");
        var expect = raw.Where(a => a != RunAction.Of(RunActionKind.ChooseRestOption, smith)).ToList();
        for (int d = 0; d < deck.Count; d++)
        {
            if (deck[d].IsUpgradable)
            {
                expect.Add(RunAction.Of(RunActionKind.ChooseRestOption, smith, d));
            }
        }
        var mac = RoomMacros.Candidates(sim, RoomMacroSet.Rest);
        Assert.SequenceEqual(expect, mac, "SMITH replaced by one row per upgradable deck card, appended");
        Assert.Equal(raw.Count - 1 + deck.Count, mac.Count, "a fresh deck is fully upgradable");
        Assert.True(mac.Contains(RunAction.Of(RunActionKind.ChooseRestOption, heal)), "HEAL kept");
        Assert.True(mac.Contains(RunAction.Of(RunActionKind.LeaveRestSite)) || !raw.Contains(RunAction.Of(RunActionKind.LeaveRestSite)));

        // env view: the candidate rows carry the deck card; the rest of the observation is the macros-off observation
        using var nb = new NativeBuffers(1);
        using var nbOff = new NativeBuffers(1);
        using var nbDef = new NativeBuffers(1);
        var env = MacroEnv(RoomMacroSet.Rest);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        var off = MacroEnv(RoomMacroSet.None);
        off.Restore(0, off.Pool.AddSnapshot(sim.Clone()), nbOff.Table);
        var def = NewEnv(1, 1, 0);
        def.Restore(0, def.Pool.AddSnapshot(sim.Clone()), nbDef.Table);
        AssertBuffersEqual(nbDef, nbOff, "macros None = the default env, every buffer");
        AssertSameObservation(nb, nbOff, "macros on vs off at a rest site");
        Assert.SequenceEqual(mac, CandList(nb));
        Assert.SequenceEqual(raw, CandList(nbOff));
        for (int r = 0; r < mac.Count; r++)
        {
            var row = CandRow(nb, 0, r);
            if (!RoomMacros.IsMacro(mac[r]))
            {
                Assert.True(row.SequenceEqual(CandRow(nbOff, 0, RowOf(raw, mac[r]))), $"row {r} ({mac[r]}) as without macros");
                continue;
            }
            Assert.Equal((int)RunActionKind.ChooseRestOption, row[CandCol.Kind]);
            Assert.Equal(smith, row[CandCol.A]);
            Assert.Equal(smith, row[CandCol.OptionIndex], "option_index = the SMITH rest row");
            Assert.Equal(Vocab.Card(deck[mac[r].B].Id), row[CandCol.CardId], "card_id = the deck card");
            Assert.Equal(nb.Slot(0).Deck[mac[r].B * Schema.DeckFields.Length], row[CandCol.CardId], "B = its deck row");
        }

        // one step = SMITH -> pick -> confirm, exactly the game's clicks
        var manual = ManualMacro(sim, RunActionKind.ChooseRestOption, smith, bash);
        env.Step(new[] { RowOf(mac, RunAction.Of(RunActionKind.ChooseRestOption, smith, bash)) }, nb.Table);
        var s = nb.Slot(0);
        Assert.Equal(0, s.Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap | SlotStatus.IllegalAction), "the macro step is clean");
        Assert.Equal(manual.StateHash(), env.StateHash(0), "macro = ChooseRestOption, PromptSelect, PromptConfirm");
        var after = env.Slots[0].Sim!;
        Assert.False(after.HasPendingChoice, "no prompt left: no cancel path");
        Assert.Equal(1, after.State.Players[0].Deck[bash].UpgradeLevel, "Bash upgraded");
        Assert.Equal(1, after.State.Players[0].Deck.Count(c => c.UpgradeLevel > 0), "exactly one card upgraded");
        Assert.Equal(1L, s.Episode[EpisodeCol.StepIndex], "one decision");
        var next = CandList(nb);
        Assert.False(next.Any(RoomMacros.IsMacro), "SMITH used: no macro rows left");
        Assert.False(next.Any(a => a.Kind is RunActionKind.PromptCancel or RunActionKind.PromptSelect));
    }

    public static void Test_VecCand_RoomMacroShopRemovalPaysThePrice()
    {
        var sim = ShopStart("MACRO-SHOP");
        var rs = sim.State;
        var entries = ((MerchantRoom)RoomStack.CurrentRoom(rs)!).Inventory!.AllEntries;
        int removal = entries.ToList().FindIndex(e => e is MerchantCardRemovalEntry);
        Assert.True(removal >= 0, "the shop sells card removal");
        int price = entries[removal].GetCost(rs);
        Assert.True(price > 0);
        var deck = rs.Players[0].Deck;
        int bash = deck.FindIndex(c => c.Id == "BASH");
        int n0 = deck.Count;

        var raw = RewardMacros.Candidates(sim);
        var plain = RunAction.Of(RunActionKind.BuyEntry, removal);
        Assert.True(raw.Contains(plain));
        Assert.SequenceEqual(raw, RoomMacros.Candidates(sim, RoomMacroSet.Rest), "rest macros leave the shop alone");
        var expect = raw.Where(a => a != plain).ToList();
        for (int d = 0; d < deck.Count; d++)
        {
            if (deck[d].IsRemovable)
            {
                expect.Add(RunAction.Of(RunActionKind.BuyEntry, removal, d));
            }
        }
        var mac = RoomMacros.Candidates(sim, RoomMacroSet.Shop);
        Assert.SequenceEqual(expect, mac, "removal replaced by one row per removable deck card, after LeaveShop");
        Assert.True(mac.IndexOf(RunAction.Of(RunActionKind.LeaveShop)) < mac.IndexOf(RunAction.Of(RunActionKind.BuyEntry, removal, 0)));

        using var nb = new NativeBuffers(1);
        using var nbOff = new NativeBuffers(1);
        var env = MacroEnv(BothMacros);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        var off = MacroEnv(RoomMacroSet.None);
        off.Restore(0, off.Pool.AddSnapshot(sim.Clone()), nbOff.Table);
        AssertSameObservation(nb, nbOff, "macros on vs off in a shop");
        Assert.SequenceEqual(mac, CandList(nb));
        int shopW = Schema.ShopFields.Length;
        int priceCol = Schema.Col(Schema.ShopFields, "price");
        for (int r = 0; r < mac.Count; r++)
        {
            if (RoomMacros.IsMacro(mac[r]))
            {
                var row = CandRow(nb, 0, r);
                Assert.Equal(removal, row[CandCol.OptionIndex], "option_index = the removal's shop row");
                Assert.Equal(price, nb.Slot(0).Shop[removal * shopW + priceCol], "its shop row shows the price");
                Assert.Equal(Vocab.Card(deck[mac[r].B].Id), row[CandCol.CardId]);
            }
        }

        var manual = ManualMacro(sim, RunActionKind.BuyEntry, removal, bash);
        env.Step(new[] { RowOf(mac, RunAction.Of(RunActionKind.BuyEntry, removal, bash)) }, nb.Table);
        Assert.Equal(0, nb.Slot(0).Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap | SlotStatus.IllegalAction));
        Assert.Equal(manual.StateHash(), env.StateHash(0), "macro = BuyEntry, PromptSelect, PromptConfirm");
        var p = env.Slots[0].Sim!.State.Players[0];
        Assert.False(env.Slots[0].Sim!.HasPendingChoice);
        Assert.Equal(999 - price, p.Gold, "the removal price is paid");
        Assert.Equal(n0 - 1, p.Deck.Count);
        Assert.False(p.Deck.Any(c => c.Id == "BASH"), "Bash removed");
        Assert.Equal(1, p.ExtraFields.CardShopRemovalsUsed);
        Assert.False(CandList(nb).Any(RoomMacros.IsMacro), "the removal is sold out");

        // not enough gold: neither the plain row nor the macro rows
        var poor = sim.Clone();
        poor.State.Players[0].Gold = price - 1;
        Assert.False(RoomMacros.Candidates(poor, RoomMacroSet.Shop).Any(a => a.Kind == RunActionKind.BuyEntry && a.A == removal));
    }

    /// <summary>
    /// 32 play-biased runs (every character) with both macros: no step error, macros taken at rest sites and shops, and
    /// no slot ever sits at the SMITH / merchant-removal prompt (those screens are never shown, so never cancelled).
    /// </summary>
    public static void Test_VecEnv_RoomMacrosSoak()
    {
        const int N = 32;
        var env = MacroEnv(BothMacros, N, poolCount: 64, threads: 4);
        using var nb = new NativeBuffers(N);
        env.Reset(default, default, nb.Table);
        var rng = SlotRngs(N, 77);
        var act = new int[N];
        int rest = 0, shop = 0, steps = 0;
        for (int t = 0; t < 4000; t++)
        {
            for (int i = 0; i < N; i++)
            {
                var sim = env.Slots[i].Sim;
                var site = sim?.PendingChoice?.SiteId;
                Assert.False(site is RoomPrompts.SmithSite or RoomPrompts.MerchantRemovalSite, $"slot {i} at the {site} prompt");
                act[i] = PlayBiased(nb, i, ref rng[i]);
                if (act[i] >= 0)
                {
                    var a = CandidateTable.ToAction(CandRow(nb, i, act[i]));
                    if (RoomMacros.IsMacro(a))
                    {
                        if (a.Kind == RunActionKind.ChooseRestOption) rest++;
                        else shop++;
                    }
                }
            }
            env.Step(act, nb.Table);
            steps += N;
            for (int i = 0; i < N; i++)
            {
                Assert.Equal(0, nb.Slot(i).Status & (int)SlotStatus.SimError, $"slot {i} step {t}: {env.Slots[i].LastError}");
            }
        }
        Assert.True(rest > 0, $"rest-site macros taken ({rest})");
        Assert.True(shop > 0, $"shop removal macros taken ({shop})");
    }
}
