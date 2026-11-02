using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Server;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

// UI-parity regression suite (docs/parity-audit-2026-11-02.md): the audit's checklist as assertions on sampled states
// of random runs, for the play server's observation (JSON + text + /piles) and the Vec encoder, plus the targeted
// leak / gap fixes of the audit (hidden powers, draw order, event option texts).
public static class ParityAuditTests
{
    private static readonly string[] Characters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    private static void EnsureRulesText()
    {
        if (!RulesText.Loaded)
        {
            RulesText.Load(null);
        }
    }

    // Uniform-random legal playouts (PortedOnly, every third run at A10), healed below half HP so runs go deep.
    // visit sees every decision point (live state) and returns false to stop.
    private static void Playouts(int runs, int maxSteps, int seed, Func<RunSim, bool> visit)
    {
        for (int r = 0; r < runs; r++)
        {
            var rng = new System.Random(seed * 7919 + r);
            var sim = RunSim.NewRun(new RunConfig($"PAUD-{seed}-{r}", Characters[r % Characters.Length], r % 3 == 0 ? 10 : 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < maxSteps && !sim.IsTerminal; step++)
            {
                if (!sim.HasPendingChoice && sim.State.Players[0] is { } p && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 200);
                    p.CurrentHp = p.MaxHp;
                }
                if (!visit(sim))
                {
                    return;
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
    }

    private static JsonObject Observe(RunSim sim, out GameSession g)
    {
        g = GameSession.ForRun("pa", null, new JsonObject(), sim);
        return ObservationBuilder.Build(g, ActionTokens.Enumerate(g));
    }

    private static RunState NewStandaloneRun(string seed)
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

    // Page texts whose only unresolved placeholders are flavour from tables knowledge/source does not export (the
    // "characters" table: {pronounObject} / {pronounSubject}, SunkenTreasury's {Monologue}) or repeat the option just
    // taken (EndlessConveyor's {LastDishTitle}: the dish whose option the player picked). No decision content.
    private static readonly HashSet<string> FlavorOnlyPages = new(StringComparer.Ordinal)
    {
        "REFLECTIONS.pages.SHATTER.description",
        "SUNKEN_TREASURY.pages.SECOND_CHEST.description",
        "ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.description",
    };

    // A real run state (player rngs set): what events that roll relics or potions need.
    private static RunState NewSimRun(string seed)
        => RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly }).State;

    // (a) Every option of every event (pages reachable within three choices, three seeds) renders a text without an
    // unresolved '?' placeholder: the 33 option keys that rendered '?' before the audit (SAPPHIRE_SEED, SLIPPERY_BRIDGE,
    // WOOD_CARVINGS, TINKER_TIME, TEA_MASTER, ...) resolve from read-only accessors on the event models.
    public static void Test_ParityAudit_EventOptionTextsResolved()
    {
        EnsureRulesText();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        int pages = 0;
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
                        rs = NewStandaloneRun(seed);
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
                    if (RulesText.EventPageText(ev, rs) is { } page)
                    {
                        pages++;
                        if (!FlavorOnlyPages.Contains(ev.Description ?? string.Empty) && System.Text.RegularExpressions.Regex.IsMatch(page, @"(^|\s)\?(\s|$|\[|\.)"))
                        {
                            problems.Add($"page {ev.Description}: {page}");
                        }
                    }
                    if (ev.IsFinished)
                    {
                        continue;
                    }
                    events.Add(id);
                    for (int i = 0; i < ev.CurrentOptions.Count; i++)
                    {
                        var o = ev.CurrentOptions[i];
                        if (seen.Add(o.TextKey ?? string.Empty) && o.TextKey != "PROCEED")
                        {
                            string? t = RulesText.EventOptionText(ev, o, rs);
                            if (t == null || System.Text.RegularExpressions.Regex.IsMatch(t, @"(^|\s)\?(\s|$|\[|\.)"))
                            {
                                problems.Add($"{o.TextKey}: {t ?? "null"}");
                            }
                        }
                        if (path.Length < 3)
                        {
                            queue.Enqueue(path.Append(i).ToArray());
                        }
                    }
                }
            }
        }
        Console.WriteLine($"     events: {events.Count}/{EventRegistry.AllIds.Count} walked, {seen.Count} option keys, {pages} page texts");
        Assert.True(problems.Count == 0, "unresolved event texts:\n  " + string.Join("\n  ", problems));
        Assert.True(events.Count >= 55, $"only {events.Count} events walked");
        Assert.True(seen.Count >= 190, $"only {seen.Count} option keys");
    }

    // The option values of events a standalone walk cannot reach (found by a static scan of the option templates'
    // placeholders against the event classes): RELIC_TRADER's trade pairs (decision-critical), STONE_OF_ALL_TIME's
    // potion, WELCOME_TO_WONGOS' featured relic, GRAVE_OF_THE_FORGOTTEN's enchantment.
    public static void Test_ParityAudit_EventValuesNeedingSetup()
    {
        EnsureRulesText();
        string Option(EventModel ev, RunState rs, string keySuffix)
        {
            var o = ev.CurrentOptions.FirstOrDefault(x => x.TextKey.EndsWith(keySuffix, StringComparison.Ordinal));
            Assert.NotNull(o, $"{ev.Id} offers {keySuffix}: " + string.Join(", ", ev.CurrentOptions.Select(x => x.TextKey)));
            string text = RulesText.EventOptionText(ev, o!, rs)!;
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"(^|\s)\?(\s|$|\[|\.)"), $"{o!.TextKey} resolved: {text}");
            return text;
        }

        var rs = NewSimRun("PAEV");
        var player = rs.Players[0];
        foreach (var id in new[] { "ANCHOR", "BAG_OF_MARBLES", "BLOOD_VIAL", "BRONZE_SCALES", "LANTERN", "HAPPY_FLOWER" })
        {
            RelicCmd.Obtain(rs, id, player);
        }
        var trader = (RelicTrader)EventRegistry.Create(RelicTrader.EventId);
        trader.BeginEvent(rs, player, false);
        string top = Option(trader, rs, ".TOP");
        Assert.True(top.Contains(RulesText.RelicTitle(trader.OwnedRelicIds[0])!, StringComparison.Ordinal) && top.Contains(RulesText.RelicTitle(trader.NewRelicIds[0])!, StringComparison.Ordinal),
            "the trade names both relics: " + top);

        rs = NewSimRun("PAEV2");
        player = rs.Players[0];
        PotionCmd.TryToProcure(rs, "FIRE_POTION", player);
        var stone = EventRegistry.Create(StoneOfAllTime.EventId);
        stone.BeginEvent(rs, player, false);
        Assert.True(Option(stone, rs, ".LIFT").Contains(RulesText.PotionTitle("FIRE_POTION")!, StringComparison.Ordinal), "the potion to lose is named");

        rs = NewSimRun("PAEV3");
        rs.Players[0].Gold = 500;
        var wongos = (WelcomeToWongos)EventRegistry.Create(WelcomeToWongos.EventId);
        wongos.BeginEvent(rs, rs.Players[0], false);
        Assert.True(Option(wongos, rs, ".FEATURED_ITEM").Contains(RulesText.RelicTitle(wongos.FeaturedItemId!)!, StringComparison.Ordinal), "the featured relic is named");

        rs = NewSimRun("PAEV4");
        var fuel = new CardInstance(CardRegistry.Get("FUEL"), rs.AllocateCardInstanceId());
        fuel.AttachTo(rs.Players[0]);
        rs.Players[0].Deck.Add(fuel);
        var grave = EventRegistry.Create(GraveOfTheForgotten.EventId);
        grave.BeginEvent(rs, rs.Players[0], false);
        Option(grave, rs, ".CONFRONT");
    }

    // (c) A power the game never draws (PowerModel.IsVisibleInternal false: AMBERGRIS_POWER) appears nowhere: not in
    // the observation JSON or text, not in the step log, not in the Vec power table. Before the audit the server's
    // strict visibility check threw for AMBERGRIS and its catch showed the power.
    public static void Test_ParityAudit_HiddenPowerNeverShown()
    {
        var s = new PlayService(new PlayServiceOptions { Version = "test", CrashLogPath = null });
        var created = s.Handle("POST", "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" }.ToJsonString());
        Assert.Equal(200, created.Status, created.Json);
        var g = s.Find("c1")!;
        var cs = g.LiveCombat!;
        var power = PowerCmd.Apply(cs, "AMBERGRIS_POWER", cs.Player, 1m, cs.Player, null);
        Assert.NotNull(power, "AMBERGRIS_POWER applied");
        Assert.False(PlayerView.IsVisibleToPlayer(cs, power!), "the player-view rule hides it");
        Assert.True(Describe.IsHiddenPowerId("AMBERGRIS_POWER"), "hidden by id for the step log");
        var obs = s.Handle("GET", "/games/c1", null).Body["observation"]!.AsObject();
        Assert.False(obs.ToJsonString().Contains("AMBERGRIS", StringComparison.Ordinal), "no AMBERGRIS in the observation: " + obs["player"]!["powers"]!.ToJsonString());
        var step = s.Handle("POST", "/games/c1/step", new JsonObject { ["token"] = "end" }.ToJsonString());
        Assert.Equal(200, step.Status, step.Json);
        Assert.False(step.Body.ToJsonString().Contains("AMBERGRIS", StringComparison.Ordinal), "no AMBERGRIS in the step log or the next observation");

        using var nb = new NativeBuffers(1);
        bool done = false;
        Playouts(10, 400, 5, sim =>
        {
            if (sim.HasPendingChoice || sim.State.Combat is not { IsInProgress: true } live)
            {
                return true;
            }
            var clone = sim.Clone();
            var ccs = clone.State.Combat!;
            PowerCmd.Apply(ccs, "AMBERGRIS_POWER", ccs.Player, 1m, ccs.Player, null);
            var st = SlotStatus.Ok;
            ObservationEncoder.Encode(clone, nb.Slot(0), ref st);
            int amb = Vocab.Power("AMBERGRIS_POWER");
            var table = nb.All<int>(Buf.PowersPlayer);
            for (int i = 0; i < Caps.PlayerPowers; i++)
            {
                Assert.NotEqual(amb, table[i * 3], "Vec never encodes AMBERGRIS_POWER");
            }
            var o = Observe(clone, out _);
            Assert.False(o.ToJsonString().Contains("AMBERGRIS", StringComparison.Ordinal), "run observation hides it too");
            done = true;
            return false;
        });
        Assert.True(done, "reached a combat");
    }

    // (H1) The draw order never reaches the play server's output: reshuffling the draw pile of a combat state leaves
    // the observation (JSON, text, legal actions) and GET /piles byte-identical.
    public static void Test_ParityAudit_ServerDrawOrderHidden()
    {
        int tested = 0;
        int visits = 0;
        Playouts(40, 900, 41, sim =>
        {
            if (visits++ % 4 != 0 || sim.HasPendingChoice || sim.State.Combat is not { IsInProgress: true } cs || cs.Player.DrawPile.Count < 2)
            {
                return true;
            }
            var a = sim.Clone();
            var b = sim.Clone();
            var cards = b.State.Combat!.Player.DrawPile.MutableCards;
            var shuffle = new System.Random(visits);
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = shuffle.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
            var oa = Observe(a, out var ga);
            var ob = Observe(b, out var gb);
            Assert.Equal(oa.ToJsonString(), ob.ToJsonString(), "observation independent of the draw order");
            Assert.Equal(ObservationBuilder.PilesNode(ga).ToJsonString(), ObservationBuilder.PilesNode(gb).ToJsonString(), "/piles independent of the draw order");
            tested++;
            return tested < 250;
        });
        Console.WriteLine($"     server draw-order leak test: {tested} reshuffled combat states, identical outputs");
        Assert.True(tested >= 150, $"only {tested} reshuffled combat states");
    }

    private static List<(string Id, int Amount)> JsonPowers(JsonNode? powers)
        => (powers as JsonArray ?? new JsonArray()).Select(p => (p!["id"]!.GetValue<string>(), p["amount"]!.GetValue<int>())).ToList();

    private static List<(string Id, int Amount)> VecPowers(Span<int> table, int rows, int width)
    {
        var list = new List<(string, int)>();
        for (int i = 0; i < rows; i++)
        {
            if (table[i * width] != 0)
            {
                list.Add((Vocab.Powers.NameOf(table[i * width]), table[i * width + 1]));
            }
        }
        return list;
    }

    // The audit checklist on sampled states: every element a human sees is in the server JSON and text, and the Vec
    // encoder agrees with the server on each element both carry (docs/parity-audit-2026-11-02.md, "Regression suite").
    public static void Test_ParityAudit_SampledStatesChecklist()
    {
        EnsureRulesText();
        using var nb = new NativeBuffers(1);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int visits = 0;
        int sampled = 0;
        void Count(string k) => counts[k] = counts.GetValueOrDefault(k) + 1;
        Playouts(45, 2500, 53, sim =>
        {
            visits++;
            var phase = sim.Phase;
            bool rare = !sim.HasPendingChoice && phase is RunPhase.Event or RunPhase.RestSite or RunPhase.Shop or RunPhase.Map or RunPhase.Treasure;
            if (!rare && visits % 7 != 0)
            {
                return true;
            }
            var st = SlotStatus.Ok;
            ObservationEncoder.Encode(sim, nb.Slot(0), ref st);
            var gi = nb.All<int>(Buf.GlobalI);
            var obs = Observe(sim.Clone(), out var g);
            var rs = sim.State;
            string text = obs["text"]!.GetValue<string>();
            string where = $"sample {sampled} ({phase}, floor {rs.TotalFloor})";
            sampled++;

            // run header: act, floor, ascension, HP, gold
            Assert.Equal(rs.Ascension, obs["ascension"]!.GetValue<int>(), where + " ascension json");
            Assert.Equal(rs.Ascension, gi[GI.Ascension], where + " ascension vec");
            Assert.True(text.Split('\n')[0].Contains($"| ascension {rs.Ascension} |", StringComparison.Ordinal), where + " header ascension: " + text.Split('\n')[0]);
            Assert.True(text.Split('\n')[0].Contains($" HP {rs.Players[0].CurrentHp}/{rs.Players[0].MaxHp}", StringComparison.Ordinal), where + " header hp");

            // bosses: map boss points + top bar, same in JSON, text and Vec
            var bosses = (obs["bosses"] as JsonArray)!.Select(b => b!["id"]!.GetValue<string>()).ToList();
            var vecBosses = new[] { gi[GI.BossId], gi[GI.SecondBossId] }.Where(v => v != 0).Select(v => Vocab.Encounters.NameOf(v)).ToList();
            Assert.SequenceEqual(vecBosses, bosses, where + " bosses json vs vec");
            Assert.True(bosses.Count > 0 && text.Contains("\nboss: " + bosses[0], StringComparison.Ordinal), where + " boss line");
            // the act's Ancient (its portrait marks the ancient map point)
            string? ancient = rs.Act.Rooms.AncientId;
            Assert.Equal(ancient, obs["ancient"]?["id"]?.GetValue<string>(), where + " ancient json");
            if (ancient != null)
            {
                Assert.True(text.Contains(" | ancient: " + ancient, StringComparison.Ordinal), where + " ancient on the boss line");
            }

            // relics (counters) and potions
            var relicNodes = obs["player"]!["relics"]!.AsArray();
            var relicRows = nb.All<int>(Buf.Relics);
            for (int i = 0; i < Math.Min(relicNodes.Count, Caps.Relics); i++)
            {
                Assert.Equal(relicNodes[i]!["id"]!.GetValue<string>(), Vocab.Relics.NameOf(relicRows[i * 5]), where + $" relic {i}");
                Assert.Equal(relicNodes[i]!["counter"]?.GetValue<int>() ?? -1, relicRows[i * 5 + 1], where + $" relic {i} counter");
            }
            var potionNodes = obs["player"]!["potions"]!.AsArray();
            var potionRows = nb.All<int>(Buf.Potions);
            Assert.Equal(potionNodes.Count, gi[GI.PotionSlots], where + " potion slots");
            for (int i = 0; i < Math.Min(potionNodes.Count, Caps.Potions); i++)
            {
                string? pid = potionNodes[i]?["id"]?.GetValue<string>();
                Assert.Equal(pid ?? Vocab.Potions.NameOf(0), Vocab.Potions.NameOf(potionRows[i * 3]), where + $" potion {i}");
            }

            // map: every node with type, visited, current, edges, quest; Vec agrees per cell
            if (obs["map"] is JsonObject map)
            {
                Count("map");
                var cells = nb.All<int>(Buf.MapNodes);
                foreach (var n in map["nodes"]!.AsArray())
                {
                    int row = n!["row"]!.GetValue<int>();
                    int col = n["col"]!.GetValue<int>();
                    Assert.True(n.AsObject().ContainsKey("quest"), where + " map node quest field");
                    if (row >= Caps.MapRows || col >= Caps.MapCols)
                    {
                        continue;
                    }
                    var cell = cells.Slice((row * Caps.MapCols + col) * 6, 6);
                    Assert.Equal(n["type"]!.GetValue<string>(), ((MapPointType)cell[0]).ToString(), where + $" map r{row}c{col} type");
                    Assert.Equal(n["visited"]!.GetValue<bool>() || n["current"]!.GetValue<bool>(), cell[1] == 1 || cell[2] == 1, where + $" map r{row}c{col} visited");
                    Assert.Equal(n["quest"]!.GetValue<int>(), cell[5], where + $" map r{row}c{col} quest");
                }
            }

            // combat: powers (visible set + icon numbers), orbs, piles
            if (obs["combat"] is JsonObject combat && rs.Combat is { IsInProgress: true } cs)
            {
                Count("combat");
                var player = cs.Player;
                var visible = player.Powers.Where(pw => PlayerView.IsVisibleToPlayer(cs, pw)).Select(pw => pw.Id).ToList();
                var shown = JsonPowers(obs["player"]!["powers"]);
                Assert.SequenceEqual(visible, shown.Select(x => x.Id).ToList(), where + " player powers = the visible set");
                Assert.SequenceEqual(shown.Take(Caps.PlayerPowers).ToList(), VecPowers(nb.All<int>(Buf.PowersPlayer), Caps.PlayerPowers, 3), where + " player powers json vs vec");
                var enemyPowers = nb.All<int>(Buf.EnemyPowers);
                foreach (var e in combat["enemies"]!.AsArray())
                {
                    int j = e!["index"]!.GetValue<int>();
                    var m = cs.Monsters[j];
                    Assert.SequenceEqual(m.Powers.Where(pw => PlayerView.IsVisibleToPlayer(cs, pw)).Select(pw => pw.Id).ToList(),
                        JsonPowers(e["powers"]).Select(x => x.Id).ToList(), where + $" enemy {j} powers = the visible set");
                    if (j < Caps.Enemies)
                    {
                        Assert.SequenceEqual(JsonPowers(e["powers"]).Take(Caps.EnemyPowers).ToList(),
                            VecPowers(enemyPowers.Slice(j * Caps.EnemyPowers * 2, Caps.EnemyPowers * 2), Caps.EnemyPowers, 2), where + $" enemy {j} powers json vs vec");
                    }
                    foreach (var it in e["intents"]!.AsArray())
                    {
                        Assert.True(it!["move"] == null && it["move_name"] == null, where + " intent without move identity");
                    }
                }
                // orbs: passive / evoke in JSON, text and Vec
                var orbNodes = obs["player"]!["orbs"] as JsonArray;
                if (orbNodes is { Count: > 0 })
                {
                    Count("orbs");
                    var orbRows = nb.All<int>(Buf.Orbs);
                    for (int i = 0; i < Math.Min(orbNodes.Count, Caps.Orbs); i++)
                    {
                        Assert.Equal(orbNodes[i]!["id"]!.GetValue<string>(), Vocab.Orbs.NameOf(orbRows[i * 3]), where + $" orb {i}");
                        Assert.Equal(orbNodes[i]!["passive"]!.GetValue<int>(), orbRows[i * 3 + 1], where + $" orb {i} passive");
                        Assert.Equal(orbNodes[i]!["evoke"]!.GetValue<int>(), orbRows[i * 3 + 2], where + $" orb {i} evoke");
                    }
                    var o0 = orbNodes[0]!;
                    Assert.True(text.Contains($"; passive/evoke): {o0["id"]!.GetValue<string>()} {o0["passive"]!.ToJsonString()}/{o0["evoke"]!.ToJsonString()}", StringComparison.Ordinal), where + " orbs line");
                }
                // piles: draw sorted multiset, discard / exhaust in pile order; counts agree with the header and Vec
                var view = combat["pile_view"]!.AsObject();
                int Sum(string k) => view[k]!.AsArray().Sum(x => x!["n"]!.GetValue<int>());
                Assert.Equal(player.DrawPile.Count, Sum("draw"), where + " draw pile view count");
                Assert.Equal(player.DiscardPile.Count, Sum("discard"), where + " discard pile view count");
                Assert.Equal(player.ExhaustPile.Count, Sum("exhaust"), where + " exhaust pile view count");
                var drawLabels = view["draw"]!.AsArray().Select(x => x!["card"]!.GetValue<string>()).ToList();
                Assert.SequenceEqual(drawLabels.OrderBy(x => x, StringComparer.Ordinal).ToList(), drawLabels, where + " draw pile sorted");
                var discardExpanded = view["discard"]!.AsArray().SelectMany(x => Enumerable.Repeat(x!["card"]!.GetValue<string>(), x["n"]!.GetValue<int>())).ToList();
                Assert.SequenceEqual(player.DiscardPile.Cards.Select(ObservationBuilder.CardLabel).ToList(), discardExpanded, where + " discard in pile order");
                Assert.Equal(player.DrawPile.Count, nb.All<short>(Buf.DrawCounts).ToArray().Sum(x => (int)x), where + " vec draw counts");
                Assert.Equal(player.ExhaustPile.Count, nb.All<short>(Buf.ExhaustCounts).ToArray().Sum(x => (int)x), where + " vec exhaust counts");
                if (player.DrawPile.Count > 0)
                {
                    Assert.True(text.Contains("\ndraw pile (sorted): ", StringComparison.Ordinal), where + " draw pile line");
                }
                if (player.DiscardPile.Count > 0)
                {
                    Assert.True(text.Contains("\ndiscard pile (oldest first): ", StringComparison.Ordinal), where + " discard pile line");
                }
                if (player.ExhaustPile.Count > 0)
                {
                    Count("exhaust");
                    Assert.True(text.Contains("\nexhaust pile (oldest first): ", StringComparison.Ordinal), where + " exhaust pile line");
                }
            }

            // rest site: every option has its description, Vec agrees on ids / enabled
            if (obs["rest"] is JsonObject rest)
            {
                Count("rest");
                var rows = nb.All<int>(Buf.Rest);
                var opts = rest["options"]!.AsArray();
                Assert.Equal(opts.Count, gi[GI.RestOptionN], where + " rest option count");
                for (int i = 0; i < opts.Count; i++)
                {
                    Assert.True(opts[i]!["text"] is JsonValue, where + $" rest option {opts[i]!["id"]} has text");
                    Assert.Equal(opts[i]!["id"]!.GetValue<string>(), Vocab.RestOptions.NameOf(rows[i * 2]), where + $" rest option {i}");
                    Assert.Equal(opts[i]!["enabled"]!.GetValue<bool>(), rows[i * 2 + 1] == 1, where + $" rest option {i} enabled");
                }
            }

            // event: option texts resolved, Vec agrees on the count
            if (obs["event"] is JsonObject ev)
            {
                Count("event");
                var opts = ev["options"]!.AsArray();
                if (!ev["finished"]!.GetValue<bool>())
                {
                    Assert.Equal(opts.Count, gi[GI.EventOptionN], where + " event option count");
                }
                foreach (var o in opts)
                {
                    string? t = o!["text"]?.GetValue<string>();
                    Assert.False(t != null && System.Text.RegularExpressions.Regex.IsMatch(t, @"(^|\s)\?(\s|$|\[|\.)"), where + $" event option text resolved: {t}");
                }
            }

            // shop: prices and sale flags as shown, Vec agrees per entry
            if (obs["shop"] is JsonObject shop)
            {
                Count("shop");
                var rows = nb.All<int>(Buf.Shop);
                foreach (var e in shop["entries"]!.AsArray())
                {
                    int i = e!["index"]!.GetValue<int>();
                    if (i >= Caps.Shop)
                    {
                        continue;
                    }
                    Assert.Equal(e["price"]!.GetValue<int>(), rows[i * 7 + 3], where + $" shop {i} price");
                    Assert.Equal(!e["sold"]!.GetValue<bool>(), rows[i * 7 + 4] == 1, where + $" shop {i} stocked");
                    Assert.Equal(e["on_sale"]!.GetValue<bool>(), rows[i * 7 + 5] == 1, where + $" shop {i} on sale");
                }
            }

            // upgrade prompts preview the upgraded card (NDeckUpgradeSelectScreen / NUpgradePreview)
            if (obs["prompt"] is JsonObject pr && pr["kind"]!.GetValue<string>() is "HandUpgrade" or "DeckUpgrade")
            {
                Count("upgrade_prompt");
                foreach (var o in pr["options"]!.AsArray())
                {
                    Assert.True(o!["upgrade_text"] is JsonValue, where + $" upgrade preview for {o["id"]}");
                }
                Assert.True(text.Contains("\n     upgraded: ", StringComparison.Ordinal), where + " upgrade preview line");
            }

            // treasure: the relic only once the chest is open
            if (obs["treasure"] is JsonObject t2)
            {
                Count("treasure");
                if (!t2["chest_opened"]!.GetValue<bool>())
                {
                    Assert.Null(t2["relic"], where + " treasure relic hidden until open");
                    Assert.Equal(0, gi[GI.TreasureRelicId], where + " vec treasure relic hidden");
                }
            }
            return sampled < 1500;
        });
        Console.WriteLine($"     checklist: {sampled} states; " + string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
        Assert.True(counts.GetValueOrDefault("combat") >= 200, "combat states");
        Assert.True(counts.GetValueOrDefault("map") >= 50, "map states");
        Assert.True(counts.GetValueOrDefault("event") >= 20, "event states");
        Assert.True(counts.GetValueOrDefault("rest") >= 5, "rest states");
        Assert.True(counts.GetValueOrDefault("shop") >= 5, "shop states");
        Assert.True(counts.GetValueOrDefault("orbs") >= 10, "states with orbs");
    }
}
