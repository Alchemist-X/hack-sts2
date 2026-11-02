using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Server;

namespace Sts2Sim.Tests;

// Rules text (knowledge/source/*/ localisation), the typed catalog, prompt pick enumeration and the run-screen
// fields added in wave E1. Loads the committed knowledge files found by walking up from the test binary.
public static class ServerRulesTextTests
{
    private static PlayService NewService() => new(new PlayServiceOptions { Version = "test", CrashLogPath = null });

    private static void EnsureLoaded()
    {
        if (!RulesText.Loaded)
        {
            RulesText.Load(null);
        }
        Assert.True(RulesText.Loaded, "rules text loads from the committed knowledge/source: " + RulesText.Summary);
    }

    private static JsonObject Post(PlayService s, string path, JsonObject body, int expected = 200)
    {
        var r = s.Handle("POST", path, body.ToJsonString());
        Assert.Equal(expected, r.Status, $"POST {path}: {r.Json}");
        return r.Body.AsObject();
    }

    private static JsonObject Get(PlayService s, string path, int expected = 200)
    {
        var r = s.Handle("GET", path, null);
        Assert.Equal(expected, r.Status, $"GET {path}: {r.Json}");
        return r.Body.AsObject();
    }

    private static List<string> Tokens(JsonObject obs)
        => obs["legal_actions"]!.AsArray().Select(a => a!["token"]!.GetValue<string>()).ToList();

    private static void Clean(string? text, string what)
    {
        Assert.NotNull(text, what + " has text");
        Assert.False(text!.Contains('{') || text.Contains('}'), $"{what}: no unformatted placeholder in '{text}'");
        Assert.False(text.Contains("[gold]") || text.Contains("[/") || text.Contains("[blue]"), $"{what}: markup stripped in '{text}'");
        Assert.False(text.Contains('\n'), $"{what}: one line");
    }

    public static void Test_RulesText_LoaderResolvesCardsRelicsPotionsKeywords()
    {
        EnsureLoaded();
        Assert.True(RulesText.Versions.Contains("v0.107.1"), "v0.107.1 loaded: " + RulesText.Summary);

        Assert.Equal("Perfected Strike", RulesText.CardTitle("PERFECTED_STRIKE"));
        var strike = new CardInstance(CardRegistry.Get("PERFECTED_STRIKE"), 1);
        string damage = strike.Vars.TryGet("CalculatedDamage")!.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string extra = strike.Vars.TryGet("ExtraDamage")!.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string text = RulesText.CardText(strike)!;
        Clean(text, "PERFECTED_STRIKE");
        Assert.True(text.StartsWith($"Deal {damage} damage. Deals {extra} additional damage", StringComparison.Ordinal), "PERFECTED_STRIKE text: " + text);
        strike.Upgrade();
        string upExtra = strike.Vars.TryGet("ExtraDamage")!.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotEqual(extra, upExtra, "upgrade raises ExtraDamage");
        Assert.True(RulesText.CardText(strike)!.Contains($"Deals {upExtra} additional", StringComparison.Ordinal), "upgraded text uses the instance var");

        Assert.Equal("Burning Blood", RulesText.RelicTitle("BURNING_BLOOD"));
        var blood = RelicRegistry.Create("BURNING_BLOOD");
        string heal = blood.DynamicVars.TryGet("Heal")!.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"At the end of combat, heal {heal} HP.", RulesText.RelicText("BURNING_BLOOD"));
        Assert.Equal(RulesText.RelicText("BURNING_BLOOD"), RulesText.RelicText(blood), "id and instance agree");

        Assert.Equal("Block Potion", RulesText.PotionTitle("BLOCK_POTION"));
        var potion = PotionRegistry.Create("BLOCK_POTION");
        string block = potion.DynamicVars.TryGet("Block")!.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Gain {block} Block.", RulesText.PotionText("BLOCK_POTION"));

        Clean(RulesText.KeywordText("exhaust"), "keyword exhaust");
        Assert.Equal("Removed until the end of combat.", RulesText.KeywordText("Exhaust"));
        Assert.NotNull(RulesText.KeywordText("VULNERABLE"), "v0.107.1-only keyword still present (per-id merge)");

        Assert.Null(RulesText.CardTitle("NO_SUCH_CARD"));
        Assert.Null(RulesText.RelicText("NO_SUCH_RELIC"));
    }

    public static void Test_RulesText_TemplateFormatters()
    {
        EnsureLoaded();
        // show (IfUpgraded): True Grit exhausts at random only before the upgrade.
        var grit = new CardInstance(CardRegistry.Get("TRUE_GRIT"), 1);
        Assert.True(RulesText.CardText(grit)!.Contains("at random", StringComparison.Ordinal), "TRUE_GRIT base: " + RulesText.CardText(grit));
        grit.Upgrade();
        Assert.False(RulesText.CardText(grit)!.Contains("at random", StringComparison.Ordinal), "TRUE_GRIT+: " + RulesText.CardText(grit));
        // energy icons render as [E]
        foreach (var id in new[] { "ADRENALINE" })
        {
            if (CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef)
            {
                string t = RulesText.CardText(new CardInstance(def, 1))!;
                Clean(t, id);
                Assert.True(t.Contains(RulesText.EnergyIcon, StringComparison.Ordinal), id + " energy icon: " + t);
            }
        }
        // InCombat-only suffixes are dropped out of combat.
        if (CardRegistry.TryGet("BODY_SLAM", out var bodySlam) && bodySlam is not UnportedCardDef)
        {
            string t = RulesText.CardText(new CardInstance(bodySlam, 1))!;
            Clean(t, "BODY_SLAM");
            Assert.False(t.Contains("(Deals", StringComparison.Ordinal), "BODY_SLAM out of combat: " + t);
        }
        // percentMore: Bowler Hat GoldIncrease 1.25 -> 25%
        var hat = RelicRegistry.Create("BOWLER_HAT");
        int pct = (int)((hat.DynamicVars.TryGet("GoldIncrease")!.BaseValue - 1m) * 100m);
        Assert.True(RulesText.RelicText("BOWLER_HAT")!.Contains(pct + "%", StringComparison.Ordinal), "BOWLER_HAT: " + RulesText.RelicText("BOWLER_HAT"));
        // powers: amount-aware cond/abs, and "N" without an amount
        Assert.Equal("Strength", RulesText.PowerTitle("STRENGTH_POWER"));
        Assert.Equal("Increases attack damage by 3.", RulesText.PowerText("STRENGTH_POWER", 3));
        Assert.Equal("Decreases attack damage by 2.", RulesText.PowerText("STRENGTH_POWER", -2));
        Assert.Equal("Increases attack damage by N.", RulesText.PowerText("STRENGTH_POWER"));
        Assert.Equal(RulesText.PowerText("STRENGTH_POWER"), RulesText.PowerText("STRENGTH"), "id with or without _POWER");
        Clean(RulesText.PowerText("VULNERABLE_POWER", 2), "VULNERABLE");
        // temporary powers borrow the generic TEMPORARY_STRENGTH text and the origin card's title
        Assert.Equal(RulesText.CardTitle("SETUP_STRIKE"), RulesText.PowerTitle("SETUP_STRIKE_POWER"));
        Clean(RulesText.PowerText("SETUP_STRIKE_POWER", 2), "SETUP_STRIKE_POWER");
    }

    public static void Test_RulesText_EnemiesAndEvents()
    {
        EnsureLoaded();
        Assert.Equal("Nibbit", RulesText.EnemyTitle("NIBBIT"));
        Assert.Equal("Butt", RulesText.EnemyMoveTitle("NIBBIT", "BUTT"));
        Assert.Equal("Butt", RulesText.EnemyMoveTitle("NIBBIT", "BUTT_MOVE"), "move ids with the sim's _MOVE suffix");
        Assert.Equal("Neow", RulesText.EventTitle("NEOW"));
        Assert.NotNull(RulesText.EventTitle("ABYSSAL_BATHS"));
        string raw = RulesText.EventOptionText("ABYSSAL_BATHS", "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE")!;
        Assert.True(raw.StartsWith("Immerse: ", StringComparison.Ordinal) && raw.Contains('?'), "no model: placeholders '?': " + raw);
        var ev = EventRegistry.Create("ABYSSAL_BATHS");
        var opt = new EventOption("ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", null);
        string live = RulesText.EventOptionText(ev, opt)!;
        Assert.Equal($"Immerse: Gain {(int)AbyssalBaths.MaxHpGain} Max HP. Take {AbyssalBaths.BaseDamage} damage.", live, "vars from the event model");
        string abstain = RulesText.EventOptionText(ev, new EventOption("ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN", null))!;
        Assert.Equal($"Abstain: Heal {(int)AbyssalBaths.HealAmount} HP.", abstain);
        // ancient relic options fall back to the relic's title and text
        string neow = RulesText.EventOptionText("NEOW", "NEOW.pages.INITIAL.options.GOLDEN_PEARL")!;
        Assert.True(neow.StartsWith(RulesText.RelicTitle("GOLDEN_PEARL")! + ": ", StringComparison.Ordinal), "relic option: " + neow);
    }

    public static void Test_RulesText_LoadNeverThrows()
    {
        try
        {
            RulesText.Load("/nonexistent/knowledge/dir");
            Assert.False(RulesText.Loaded, "missing dir leaves rules text unloaded");
            Assert.Null(RulesText.CardTitle("STRIKE_IRONCLAD"));
            Assert.True(RulesText.Summary.Contains("nothing found", StringComparison.Ordinal), RulesText.Summary);
        }
        finally
        {
            RulesText.Load(null);
        }
        Assert.True(RulesText.Loaded, "reloaded");
    }

    public static void Test_RulesText_Coverage()
    {
        EnsureLoaded();
        int cards = 0, cardsText = 0, cardsQ = 0;
        var cardMissing = new List<string>();
        foreach (var id in CardRegistry.AllIds)
        {
            if (!CardRegistry.TryGet(id, out var def) || def is UnportedCardDef)
            {
                continue;
            }
            cards++;
            string? t = RulesText.CardText(id, 0);
            if (t != null)
            {
                cardsText++;
                if (t.Contains('?'))
                {
                    cardsQ++;
                    cardMissing.Add(id + "(?: " + t + ")");
                }
            }
            else
            {
                cardMissing.Add(id);
            }
        }
        int relics = 0, relicsText = 0, relicsQ = 0;
        var relicMissing = new List<string>();
        foreach (var id in RelicRegistry.AllIds)
        {
            relics++;
            string? t = RulesText.RelicText(id);
            if (t != null)
            {
                relicsText++;
                if (t.Contains('?'))
                {
                    relicsQ++;
                    relicMissing.Add(id + "(?: " + t + ")");
                }
            }
            else
            {
                relicMissing.Add(id);
            }
        }
        int potions = 0, potionsText = 0;
        var potionMissing = new List<string>();
        foreach (var id in PotionRegistry.AllIds)
        {
            potions++;
            if (RulesText.PotionText(id) != null)
            {
                potionsText++;
            }
            else
            {
                potionMissing.Add(id);
            }
        }
        int powers = 0, powersText = 0;
        var powerMissing = new List<string>();
        foreach (var id in PowerRegistry.AllIds)
        {
            powers++;
            if (RulesText.PowerText(id, 1) != null)
            {
                powersText++;
            }
            else
            {
                powerMissing.Add(id);
            }
        }
        int events = 0, eventsTitled = 0;
        foreach (var id in EventRegistry.AllIds)
        {
            events++;
            eventsTitled += RulesText.EventTitle(id) != null ? 1 : 0;
        }
        int monsters = 0, monstersTitled = 0;
        var monsterMissing = new List<string>();
        foreach (var id in MonsterRegistry.AllIds)
        {
            monsters++;
            if (RulesText.EnemyTitle(id) != null)
            {
                monstersTitled++;
            }
            else
            {
                monsterMissing.Add(id);
            }
        }
        Console.WriteLine($"    rules text coverage: cards {cardsText}/{cards} ({cardsQ} with '?'), relics {relicsText}/{relics} ({relicsQ} with '?'), potions {potionsText}/{potions}, powers {powersText}/{powers}, events {eventsTitled}/{events} titled, monsters {monstersTitled}/{monsters} titled");
        Console.WriteLine("    card text missing or with ?: " + string.Join(",", cardMissing));
        Console.WriteLine("    relic text missing or with ?: " + string.Join(",", relicMissing));
        Console.WriteLine("    no potion text: " + string.Join(",", potionMissing));
        Console.WriteLine("    no power text: " + string.Join(",", powerMissing));
        Console.WriteLine("    no monster title: " + string.Join(",", monsterMissing));
        Assert.True(cardsText >= cards * 95 / 100, "cards with text");
        Assert.True(relicsText >= relics * 95 / 100, "relics with text");
        Assert.True(potionsText >= potions * 95 / 100, "potions with text");
        Assert.True(powersText >= powers * 90 / 100, "powers with text");
        Assert.True(eventsTitled >= events * 90 / 100, "events with titles");
    }

    public static void Test_Server_CatalogKinds()
    {
        EnsureLoaded();
        var s = NewService();
        var old = Get(s, "/catalog");
        foreach (var key in new[] { "characters", "encounters", "cards", "relics", "potions", "events" })
        {
            Assert.True(old[key] is JsonArray, "untyped /catalog keeps " + key);
        }
        var cards = Get(s, "/catalog?kind=cards&ids=PERFECTED_STRIKE,BASH");
        Assert.Equal("cards", cards["kind"]!.GetValue<string>());
        var items = cards["items"]!.AsArray();
        Assert.Equal(2, items.Count, "ids filter");
        var ps = items.First(i => i!["id"]!.GetValue<string>() == "PERFECTED_STRIKE")!;
        Assert.Equal("Perfected Strike", ps["name"]!.GetValue<string>());
        Clean(ps["text"]!.GetValue<string>(), "catalog PERFECTED_STRIKE");
        foreach (var kind in RulesText.Kinds)
        {
            var all = Get(s, "/catalog?kind=" + kind)["items"]!.AsArray();
            Assert.True(all.Count > 0, kind + " catalog not empty");
            Assert.True(all.All(i => i is JsonObject o && o.ContainsKey("id") && o.ContainsKey("name") && o.ContainsKey("text")), kind + " items have id/name/text");
        }
        var grep = Get(s, "/catalog?kind=powers&grep=attack%20damage")["items"]!.AsArray();
        Assert.True(grep.Any(i => i!["id"]!.GetValue<string>() == "STRENGTH_POWER"), "grep matches text (url-decoded)");
        var nibbit = Get(s, "/catalog?kind=enemies&ids=NIBBIT")["items"]!.AsArray().Single()!;
        Assert.True(nibbit["moves"]!.AsArray().Any(m => m!["title"]!.GetValue<string>() == "Butt"), "enemy moves");
        Get(s, "/catalog?kind=bogus", 400);
        var lookup = RulesText.Lookup("strength");
        Assert.True(lookup.Any(l => l.Kind == "powers" && l.Item.Name == "Strength"), "Lookup finds a power by short id");
    }

    // Play-test bug: PAELS_TOOTH DeckRemove offered 16 options but legal_actions stopped at pick 11. The kernel
    // enumerates unordered multi-pick prompts in canonical ascending order (FillPrompt), which hides the top
    // options from the first pick; the server now lists every unpicked option.
    public static void Test_Server_PromptListsEveryUnpickedOption()
    {
        var options = Enumerable.Range(0, 16).Select(i => new ChoiceOption(100 + i, "STRIKE_IRONCLAD", 0)).ToArray();
        var prompt = new ChoicePrompt(ChoicePromptKind.DeckRemove, "PAELS_TOOTH", "PAELS_TOOTH", -1, PileType.Deck, options,
            MinSelect: 5, MaxSelect: 5, RequireConfirm: false, CanSkip: false, Cancelable: false, Ordered: false, ChoiceResultType.CombatCard);
        var picks = new List<int>();
        var kernel = new List<GameAction>();
        LegalActionGenerator.FillPrompt(prompt, picks, kernel);
        int kernelMax = kernel.Where(a => a.Kind == ActionKind.ChoosePick).Max(a => a.CardIndex);
        Assert.Equal(11, kernelMax, "repro: the kernel's canonical enumeration stops at pick 11 of 16");
        var order = new[] { 15, 3, 14, 0, 9 };
        foreach (int next in order)
        {
            kernel.Clear();
            LegalActionGenerator.FillPrompt(prompt, picks, kernel);
            var listed = ActionTokens.WithAnyOrderPicks(prompt, picks, kernel.Select(RunAction.FromCombat).ToList());
            var pickIdx = listed.Where(a => a.Kind == RunActionKind.PromptSelect).Select(a => a.A).ToList();
            Assert.SequenceEqual(Enumerable.Range(0, 16).Where(i => !picks.Contains(i)), pickIdx, "every unpicked option is listed");
            Assert.True(LegalActionGenerator.TryResolvePrompt(prompt, picks, GameAction.Pick(next), out var error), "the kernel accepts pick " + next + ": " + error);
            picks.Add(next);
        }
        Assert.True(ChoiceRuntime.CompletesAutomatically(prompt, picks.Count), "the 5th pick settles the prompt");
        var ordered = prompt with { Ordered = true };
        var same = new List<RunAction> { RunAction.Of(RunActionKind.PromptSelect, 2) };
        Assert.True(ReferenceEquals(same, ActionTokens.WithAnyOrderPicks(ordered, new List<int>(), same)), "ordered prompts are left to the kernel");
    }

    // End to end: random runs until a prompt appears; its desc says how it settles, and for an unordered
    // multi-pick prompt the highest option is pickable first.
    public static void Test_Server_PromptDescAndAnyOrderPick()
    {
        EnsureLoaded();
        var s = NewService();
        bool sawPrompt = false;
        for (int seed = 1; seed <= 40 && !sawPrompt; seed++)
        {
            var random = new System.Random(seed);
            var created = Post(s, "/runs", new JsonObject { ["seed"] = "PROMPT" + seed });
            string id = created["id"]!.GetValue<string>();
            var obs = created["observation"]!.AsObject();
            for (int n = 0; n < 400 && !obs["terminal"]!.GetValue<bool>(); n++)
            {
                if (obs["prompt"] is JsonObject prompt)
                {
                    sawPrompt = true;
                    foreach (var key in new[] { "source", "source_name", "source_text", "pile", "effect", "needs_confirm", "auto_completes_at", "desc" })
                    {
                        Assert.True(prompt.ContainsKey(key), "prompt has " + key);
                    }
                    string desc = prompt["desc"]!.GetValue<string>();
                    Assert.True(desc.Contains("settles", StringComparison.Ordinal) || desc.Contains("confirm", StringComparison.Ordinal), "desc says how the prompt settles: " + desc);
                    string text = obs["text"]!.GetValue<string>();
                    Assert.True(text.Contains("   " + desc, StringComparison.Ordinal), "text renders the prompt desc");
                    int n0 = prompt["options"]!.AsArray().Count;
                    var picks = Tokens(obs).Where(t => t.StartsWith("pick ", StringComparison.Ordinal)).ToList();
                    if (prompt["picked"]!.AsArray().Count == 0 && prompt["max"]!.GetValue<int>() > 1)
                    {
                        Assert.Equal(n0, picks.Count, "an unordered multi-pick prompt lists every option: " + string.Join(",", picks));
                        StepOk(s, id, "pick " + (n0 - 1));
                    }
                    break;
                }
                var legal = Tokens(obs);
                obs = StepOk(s, id, legal[random.Next(legal.Count)])["observation"]!.AsObject();
            }
            s.Handle("DELETE", $"/games/{id}", null);
        }
        Assert.True(sawPrompt, "a prompt appeared in 40 random runs");
    }

    private static JsonObject StepOk(PlayService s, string id, string token)
        => Post(s, $"/games/{id}/step", new JsonObject { ["token"] = token });

    public static void Test_Server_RunScreensWithRulesText()
    {
        EnsureLoaded();
        var s = NewService();
        bool sawMap = false, sawCardReward = false, sawCombat = false, sawSkipKeeps = false;
        for (int seed = 1; seed <= 6; seed++)
        {
            var random = new System.Random(seed);
            var created = Post(s, "/runs", new JsonObject { ["seed"] = "SCREENS" + seed });
            string id = created["id"]!.GetValue<string>();
            var obs = created["observation"]!.AsObject();
            for (int n = 0; n < 300 && !obs["terminal"]!.GetValue<bool>(); n++)
            {
                string text = obs["text"]!.GetValue<string>();
                if (obs["map"] is JsonObject map)
                {
                    sawMap = true;
                    var nodes = map["nodes"]!.AsArray();
                    Assert.True(nodes.Count > 10, "map.nodes holds the whole act");
                    Assert.True(nodes.Count(nd => nd!["current"]!.GetValue<bool>()) <= 1, "at most one current room");
                    Assert.True(nodes.All(nd => nd is JsonObject o && o.ContainsKey("row") && o.ContainsKey("col") && o.ContainsKey("type") && o.ContainsKey("edges") && o.ContainsKey("visited")), "node fields");
                    Assert.True(text.Contains("-- act map", StringComparison.Ordinal), "text renders the act map");
                    int mapLines = text.Split('\n').SkipWhile(l => !l.StartsWith("-- act map", StringComparison.Ordinal)).Skip(1).TakeWhile(l => l.StartsWith(" r", StringComparison.Ordinal)).Count();
                    Assert.True(mapLines is > 0 and <= 19, "act map is at most 19 row lines: " + mapLines);
                }
                if (obs["card_reward"] is JsonObject cr)
                {
                    sawCardReward = true;
                    Assert.True(cr.ContainsKey("skip_consumes"), "card_reward.skip_consumes");
                    foreach (var c in cr["cards"]!.AsArray())
                    {
                        Assert.NotNull(c!["name"], "card reward card has a name");
                    }
                    if (cr["can_skip"]!.GetValue<bool>() && cr["skip_consumes"] is JsonValue sc && !sc.GetValue<bool>() && Tokens(obs).Contains("skip"))
                    {
                        // skip keeps the card reward on the rewards screen
                        var after = StepOk(s, id, "skip")["observation"]!.AsObject();
                        if (after["rewards"] is JsonArray rewards)
                        {
                            Assert.True(rewards.Any(r => r!["type"]!.GetValue<string>() == "Card"), "after skip the card reward is still listed");
                            sawSkipKeeps = true;
                        }
                        obs = after;
                        continue;
                    }
                }
                if (obs["combat"] is JsonObject combat && combat["hand"] is JsonArray hand && hand.Count > 0)
                {
                    sawCombat = true;
                    Assert.True(hand.All(c => c!["name"] != null), "hand cards have names");
                    Assert.True(text.Contains(" — ", StringComparison.Ordinal), "text renders rules text");
                }
                if (obs["shop"] is JsonObject shop)
                {
                    foreach (var e in shop["entries"]!.AsArray())
                    {
                        Assert.True(e!.AsObject().ContainsKey("on_sale") && e.AsObject().ContainsKey("base_price"), "shop entry on_sale/base_price");
                    }
                }
                var legal = Tokens(obs);
                obs = StepOk(s, id, legal[random.Next(legal.Count)])["observation"]!.AsObject();
            }
            s.Handle("DELETE", $"/games/{id}", null);
        }
        Assert.True(sawMap && sawCardReward && sawCombat, $"screens met: map {sawMap}, card reward {sawCardReward}, combat {sawCombat}");
        Assert.True(sawSkipKeeps, "skipping a card reward kept it on the rewards screen at least once");
    }
}
