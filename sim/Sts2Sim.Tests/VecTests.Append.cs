using System.Text.RegularExpressions;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Server;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// The player-view schema append (docs/parity-audit-2026-11-02.md §5.2): global_x (the act's Ancient, draw_marked_n),
/// hand_x (affliction amount, dampened), draw_marked (marked draw-pile cards, sorted: order hidden), discard_order /
/// exhaust_order (pile order with marks), event_option_values (the numbers and items an option's text names).
/// </summary>
public static unsafe partial class VecTests
{
    private static readonly int PileW = Schema.PileCardFields.Length;

    private static int[] PileRow(CardInstance card) => new[]
    {
        Vocab.Card(card.Id), card.UpgradeLevel, card.Enchantment is { } e ? Vocab.Enchantment(e.Id) : 0, card.Enchantment?.Amount ?? 0,
        card.Affliction is { } a ? Vocab.Affliction(a.Id) : 0, card.Affliction?.Amount ?? 0,
    };

    private static void Encode1(RunSim sim, NativeBuffers nb, out SlotStatus st)
    {
        st = SlotStatus.Ok;
        ObservationEncoder.Encode(sim, nb.Slot(0), ref st);
    }

    /// <summary>
    /// A first combat with marked cards in every pile and a DAMPEN debuff: hand_x, draw_marked (sorted, true count,
    /// reshuffle-invariant), discard_order / exhaust_order (pile order with marks) match the kernel; past their caps the
    /// tables keep their first CAP rows and set TABLE_OVERFLOW.
    /// </summary>
    public static void Test_VecAppend_PileTablesAndHandMarks()
    {
        var sim = FirstCombat("VEC-APPEND-PILES");
        var cs = sim.State.Combat!;
        var cp = cs.Player;
        string ench = EnchantmentRegistry.AllIds[0];
        string aff = AfflictionRegistry.AllIds[0];
        Assert.True(cp.DrawPile.Count >= 4 && cp.Hand.Count >= 3, $"draw {cp.DrawPile.Count}, hand {cp.Hand.Count}");

        var draw = cp.DrawPile.Cards;
        draw[0].EnchantInternal(EnchantmentRegistry.Create(ench), 2);
        draw[1].AfflictInternal(AfflictionRegistry.Create(aff), 3);
        draw[3].EnchantInternal(EnchantmentRegistry.Create(ench), 1);
        draw[3].AfflictInternal(AfflictionRegistry.Create(aff), 1);
        var hand = cp.Hand.Cards;
        hand[0].AfflictInternal(AfflictionRegistry.Create(aff), 2);
        CardCmd.Upgrade(cs, hand[1]);
        Assert.True(hand[1].IsUpgraded);
        var dampen = new DampenPower();
        dampen.AddCaster(cs.Monsters[0]);
        PowerCmd.Apply(cs, dampen, cp, 1, cs.Monsters[0], null);
        Assert.False(hand[1].IsUpgraded, "DAMPEN downgraded the upgraded hand card");
        // discard and exhaust: three marked/unmarked strikes each, in a known order
        var strike = CardRegistry.Get(StrikeIronclad.CardId);
        for (int i = 0; i < 3; i++)
        {
            var d = cs.CreateCard(strike, cp);
            if (i == 1)
            {
                d.EnchantInternal(EnchantmentRegistry.Create(ench), 4);
            }
            cp.DiscardPile.AddInternal(d);
            var x = cs.CreateCard(strike, cp);
            if (i == 2)
            {
                x.AfflictInternal(AfflictionRegistry.Create(aff), 5);
            }
            cp.ExhaustPile.AddInternal(x);
        }

        using var nb = new NativeBuffers(1);
        using var nb2 = new NativeBuffers(1);
        Encode1(sim, nb, out var st);
        Assert.False(st.HasFlag(SlotStatus.TableOverflow), $"status {st}");
        var s = nb.Slot(0);
        var hx = s.HandX;
        int hxw = Schema.HandXFields.Length;
        for (int i = 0; i < Caps.Hand; i++)
        {
            var row = hx.Slice(i * hxw, hxw);
            int wantAff = i < hand.Count ? hand[i].Affliction?.Amount ?? 0 : 0;
            Assert.Equal(wantAff, row[HX.AfflictionAmount], $"hand {i} affliction_amount");
            Assert.Equal(i == 1 ? 1 : 0, row[HX.Dampened], $"hand {i} dampened");
        }
        Assert.Equal(2, hx[HX.AfflictionAmount]);

        // draw_marked: the three marked draw cards, sorted by their whole row
        var expect = draw.Where(c => c.Enchantment != null || c.Affliction != null).Select(PileRow)
            .OrderBy(r => r, Comparer<int[]>.Create((a, b) => a.Zip(b, (x, y) => x.CompareTo(y)).FirstOrDefault(c => c != 0))).ToList();
        Assert.Equal(3, expect.Count);
        Assert.Equal(3, s.GlobalX[GX.DrawMarkedN]);
        for (int r = 0; r < Caps.DrawMarked; r++)
        {
            var row = s.DrawMarked.Slice(r * PileW, PileW).ToArray();
            Assert.SequenceEqual(r < expect.Count ? expect[r] : new int[PileW], row, $"draw_marked row {r}");
        }
        // pile order with marks
        foreach (var (buf, pile, name) in new[] { (Buf.DiscardOrder, cp.DiscardPile, "discard"), (Buf.ExhaustOrder, cp.ExhaustPile, "exhaust") })
        {
            var table = nb.All<int>(buf);
            for (int r = 0; r < Caps.PileOrder; r++)
            {
                var row = table.Slice(r * PileW, PileW).ToArray();
                Assert.SequenceEqual(r < pile.Count ? PileRow(pile.Cards[r]) : new int[PileW], row, $"{name}_order row {r}");
            }
        }
        Assert.Equal(4, s.DiscardOrder[PileW + PileCol.EnchantAmount], "the second discard card carries its enchantment");
        Assert.Equal(5, s.ExhaustOrder[(cp.ExhaustPile.Count - 1) * PileW + PileCol.AfflictionAmount]);

        // H1 with marks: a reshuffled draw pile encodes to the same bytes
        var clone = sim.Clone();
        var cards = clone.State.Combat!.Player.DrawPile.MutableCards;
        cards.Reverse();
        (cards[0], cards[^2]) = (cards[^2], cards[0]);
        Assert.NotEqual(clone.StateHash(), sim.StateHash());
        Encode1(clone, nb2, out var st2);
        Assert.Equal(st, st2);
        AssertBuffersEqual(nb, nb2, "reshuffled draw pile with marked cards");

        // caps: 70 exhausted cards, 40 marked draw cards
        for (int i = 0; i < 70; i++)
        {
            cp.ExhaustPile.AddInternal(cs.CreateCard(strike, cp));
        }
        for (int i = 0; i < 40; i++)
        {
            var c = cs.CreateCard(strike, cp);
            c.EnchantInternal(EnchantmentRegistry.Create(ench), 1 + i % 3);
            cp.DrawPile.AddInternal(c);
        }
        Encode1(sim, nb, out st);
        Assert.True(st.HasFlag(SlotStatus.TableOverflow), "exhaust_order / draw_marked past their caps");
        Assert.Equal(cp.ExhaustPile.Count, s.GlobalI[GI.ExhaustN], "exhaust_n keeps the true count");
        for (int r = 0; r < Caps.PileOrder; r++)
        {
            Assert.SequenceEqual(PileRow(cp.ExhaustPile.Cards[r]), nb.All<int>(Buf.ExhaustOrder).Slice(r * PileW, PileW).ToArray(), $"exhaust row {r}: first CAP rows");
        }
        Assert.Equal(43, s.GlobalX[GX.DrawMarkedN], "draw_marked_n keeps the true count");
        Assert.True(s.DrawMarked.Slice((Caps.DrawMarked - 1) * PileW, PileW)[PileCol.CardId] != 0, "draw_marked is full");
    }

    /// <summary>
    /// global_x.ancient_id is the current act's Ancient (RoomSet.AncientId, events vocabulary) on every sampled state of
    /// random runs across the three acts.
    /// </summary>
    public static void Test_VecAppend_AncientId()
    {
        using var nb = new NativeBuffers(1);
        var acts = new HashSet<(int Act, string Ancient)>();
        for (int r = 0; r < 12; r++)
        {
            var rng = new System.Random(r);
            var sim = RunSim.NewRun(new RunConfig($"VEC-ANCIENT-{r}", AllCharacters[r % AllCharacters.Length], 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < 2500 && !sim.IsTerminal; step++)
            {
                var p = sim.State.Players[0];
                if (!sim.HasPendingChoice && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 300);
                    p.CurrentHp = p.MaxHp;
                }
                if (step % 13 == 0)
                {
                    Encode1(sim, nb, out _);
                    string? ancient = sim.State.Acts.Count > 0 ? sim.State.Act.Rooms.AncientId : null;
                    int id = nb.All<int>(Buf.GlobalX)[GX.AncientId];
                    Assert.Equal(Vocab.Event(ancient), id, $"run {r} step {step}");
                    if (ancient != null)
                    {
                        Assert.True(id > VocabList.Unknown, $"ancient {ancient} is in the events vocabulary");
                        acts.Add((sim.State.CurrentActIndex, ancient));
                    }
                }
                var legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                try
                {
                    sim.Step(legal[rng.Next(legal.Count)]);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        Console.WriteLine($"     ancient_id: {string.Join(", ", acts.OrderBy(a => a.Act).Select(a => $"act {a.Act + 1} {a.Ancient}"))}");
        Assert.True(acts.Select(a => a.Act).Distinct().Count() >= 2, "ancients seen in at least two acts");
    }

    /// <summary>
    /// event_option_values against the play server's option texts: walking every event (pages within three choices,
    /// three seeds, as ParityAuditTests does) plus the four events that need setup, every number in value_k appears in
    /// the server's rendered option text and every item's title too; value_n + item_n never exceeds the option's
    /// placeholders; no event overflows the caps.
    /// </summary>
    public static void Test_VecAppend_EventOptionValuesMatchServerText()
    {
        if (!RulesText.Loaded)
        {
            RulesText.Load(null);
        }
        int w = Schema.EventOptionValueFields.Length;
        var table = new int[Caps.EventOptions * w];
        int options = 0, withValues = 0, values = 0, items = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();

        void Check(EventModel ev, RunState rs)
        {
            if (ev.IsFinished)
            {
                return;
            }
            Array.Clear(table);
            bool overflow = false;
            ObservationEncoder.EncodeEventOptionValues(ev, rs, table, ref overflow);
            if (overflow)
            {
                problems.Add($"{ev.Id}: TABLE_OVERFLOW");
            }
            for (int i = 0; i < Math.Min(ev.CurrentOptions.Count, Caps.EventOptions); i++)
            {
                var opt = ev.CurrentOptions[i];
                var row = table.AsSpan(i * w, w);
                options++;
                int nv = row[EvValCol.ValueN], ni = row[EvValCol.ItemN];
                int names = EventOptionVars.NamesOf(opt.TextKey)?.Length ?? 0;
                if (nv + ni > names)
                {
                    problems.Add($"{opt.TextKey}: {nv} values + {ni} items > {names} placeholders");
                }
                if (nv + ni == 0)
                {
                    continue;
                }
                withValues++;
                keys.Add(opt.TextKey);
                string text = RulesText.EventOptionText(ev, opt, rs) ?? "";
                for (int k = 0; k < Math.Min(nv, Caps.EventValues); k++)
                {
                    int v = row[EvValCol.Value0 + k];
                    values++;
                    if (!Regex.IsMatch(text, $@"(?<![0-9]){v}(?![0-9])"))
                    {
                        problems.Add($"{opt.TextKey}: value {v} not in \"{text}\"");
                    }
                }
                for (int k = 0; k < Math.Min(ni, Caps.EventItems); k++)
                {
                    string kind = Vocab.ItemKinds.NameOf(row[EvValCol.ItemKind0 + k * EvValCol.ItemStride]);
                    int id = row[EvValCol.ItemId0 + k * EvValCol.ItemStride];
                    items++;
                    string? title = kind switch
                    {
                        "card" => RulesText.CardTitle(Vocab.Cards.NameOf(id)),
                        "relic" => RulesText.RelicTitle(Vocab.Relics.NameOf(id)),
                        "potion" => RulesText.PotionTitle(Vocab.Potions.NameOf(id)),
                        "enchantment" => RulesText.EnchantmentTitle(Vocab.Enchantments.NameOf(id)),
                        _ => null,
                    };
                    if (title == null || !text.Contains(title, StringComparison.Ordinal))
                    {
                        problems.Add($"{opt.TextKey}: {kind} {(title ?? "?")} not in \"{text}\"");
                    }
                }
            }
        }

        foreach (var id in EventRegistry.AllIds)
        {
            foreach (var seed in new[] { "PA1", "PA2", "PA3" })
            {
                var queue = new Queue<int[]>();
                queue.Enqueue(Array.Empty<int>());
                while (queue.Count > 0)
                {
                    var path = queue.Dequeue();
                    EventModel ev;
                    RunState rs;
                    try
                    {
                        rs = StandaloneRun(seed);
                        ev = EventRegistry.Create(id);
                        ev.BeginEvent(rs, rs.Players[0], false);
                        foreach (int k in path)
                        {
                            ev.ChooseOption(rs, k);
                        }
                    }
                    catch (Exception)
                    {
                        continue; // needs a prompt answer or a combat: out of reach of a standalone walk
                    }
                    Check(ev, rs);
                    if (!ev.IsFinished && path.Length < 3)
                    {
                        for (int i = 0; i < ev.CurrentOptions.Count; i++)
                        {
                            queue.Enqueue(path.Append(i).ToArray());
                        }
                    }
                }
            }
        }

        // the events a standalone walk cannot open (ParityAuditTests.EventValuesNeedingSetup)
        var run = RunSim.NewRun(new RunConfig("PAEV", "IRONCLAD", 0) { Content = ContentFilter.PortedOnly }).State;
        foreach (var relic in new[] { "ANCHOR", "BAG_OF_MARBLES", "BLOOD_VIAL", "BRONZE_SCALES", "LANTERN", "HAPPY_FLOWER" })
        {
            RelicCmd.Obtain(run, relic, run.Players[0]);
        }
        var trader = (RelicTrader)EventRegistry.Create(RelicTrader.EventId);
        trader.BeginEvent(run, run.Players[0], false);
        Check(trader, run);
        int top = trader.CurrentOptions.ToList().FindIndex(o => o.TextKey.EndsWith(".TOP", StringComparison.Ordinal));
        Array.Clear(table);
        bool ovf = false;
        ObservationEncoder.EncodeEventOptionValues(trader, run, table, ref ovf);
        var topRow = table.AsSpan(top * w, w);
        Assert.Equal(2, topRow[EvValCol.ItemN], "RELIC_TRADER .TOP names the owned and the new relic");
        Assert.Equal(Vocab.Relic(trader.OwnedRelicIds[0]), topRow[EvValCol.ItemId0]);
        Assert.Equal(Vocab.Relic(trader.NewRelicIds[0]), topRow[EvValCol.ItemId0 + EvValCol.ItemStride]);
        Assert.Equal(Vocab.ItemKind("relic"), topRow[EvValCol.ItemKind0]);

        run = RunSim.NewRun(new RunConfig("PAEV2", "IRONCLAD", 0) { Content = ContentFilter.PortedOnly }).State;
        PotionCmd.TryToProcure(run, "FIRE_POTION", run.Players[0]);
        var stone = EventRegistry.Create(StoneOfAllTime.EventId);
        stone.BeginEvent(run, run.Players[0], false);
        Check(stone, run);

        run = RunSim.NewRun(new RunConfig("PAEV3", "IRONCLAD", 0) { Content = ContentFilter.PortedOnly }).State;
        run.Players[0].Gold = 500;
        var wongos = EventRegistry.Create(WelcomeToWongos.EventId);
        wongos.BeginEvent(run, run.Players[0], false);
        Check(wongos, run);

        Console.WriteLine($"     event_option_values: {options} option rows, {withValues} with values ({keys.Count} keys), {values} numbers and {items} items found in the server's option texts");
        Assert.True(problems.Count == 0, "event_option_values vs server text:\n  " + string.Join("\n  ", problems.Distinct().Take(40)));
        Assert.True(keys.Count >= 80, $"only {keys.Count} option keys with values");
        Assert.True(items >= 10, $"only {items} items");
    }

    private static RunState StandaloneRun(string seed)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), 0);
        foreach (var id in StartingDecks.Ironclad)
        {
            var card = new CardInstance(CardRegistry.Get(id), rs.AllocateCardInstanceId());
            card.AttachTo(player);
            player.Deck.Add(card);
        }
        player.Gold = 300;
        return rs;
    }
}
