using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Server;

namespace Sts2Sim.Tests;

// Wave E1 owner 'combat-view': stable creature uids, step log v2, effective numbers, danger line, piles route.
public static class ServerCombatViewTests
{
    private static PlayService NewService() => new(new PlayServiceOptions { Version = "test", CrashLogPath = null });

    private static JsonObject Create(PlayService s, string encounter, ulong seed, params string[] deck)
    {
        var body = new JsonObject { ["seed"] = seed, ["encounter"] = encounter, ["hp"] = 500, ["max_hp"] = 500 };
        if (deck.Length > 0)
        {
            body["deck"] = new JsonArray(deck.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
        }
        var r = s.Handle("POST", "/combats", body.ToJsonString());
        Assert.Equal(200, r.Status, r.Json);
        return r.Body["observation"]!.AsObject();
    }

    private static JsonObject Step(PlayService s, string id, string token)
    {
        var r = s.Handle("POST", $"/games/{id}/step", new JsonObject { ["token"] = token }.ToJsonString());
        Assert.Equal(200, r.Status, $"step '{token}': {r.Json}");
        return r.Body.AsObject();
    }

    private static JsonObject Observe(PlayService s, string id) => s.Handle("GET", $"/games/{id}", null).Body["observation"]!.AsObject();

    private static List<string> Log(JsonObject step) => step["log"]!.AsArray().Select(l => l!.GetValue<string>()).ToList();

    private static Dictionary<string, JsonObject> EnemiesByUid(JsonObject obs)
        => obs["combat"]!["enemies"]!.AsArray().ToDictionary(e => e!["uid"]!.GetValue<string>(), e => e!.AsObject());

    private static string PlayToken(JsonObject obs, string cardId, int enemyIndex)
    {
        var hand = obs["combat"]!["hand"]!.AsArray();
        int i = hand.Select(c => c!["id"]!.GetValue<string>()).ToList().IndexOf(cardId);
        Assert.True(i >= 0, $"{cardId} in hand");
        return $"play {i} -> {enemyIndex}";
    }

    // Every hp / spawned / died line of the log names a creature whose state really changed (no index-diff ghosts).
    private static void CheckNoGhosts(JsonObject before, JsonObject after, List<string> log)
    {
        var was = EnemiesByUid(before);
        var now = EnemiesByUid(after);
        foreach (var line in log)
        {
            // Lines about allies (pets) or dead-in-place enemies are not in the enemy lists; check the enemies'.
            var hp = Regex.Match(line, @"^(u\d+) \S+ hp (\d+) -> (\d+)");
            if (hp.Success && was.TryGetValue(hp.Groups[1].Value, out var w))
            {
                string uid = hp.Groups[1].Value;
                Assert.Equal(w["hp"]!.GetValue<int>(), int.Parse(hp.Groups[2].Value), "hp line 'from' matches: " + line);
                bool escaped = log.Any(l => l.StartsWith(uid + " ", StringComparison.Ordinal) && l.EndsWith(" escaped", StringComparison.Ordinal));
                if (!escaped)
                {
                    int to = now.TryGetValue(uid, out var n) ? n["hp"]!.GetValue<int>() : 0;
                    Assert.Equal(to, int.Parse(hp.Groups[3].Value), "hp line 'to' matches: " + line);
                }
            }
            var spawned = Regex.Match(line, @"^(u\d+) \S+ spawned");
            if (spawned.Success)
            {
                Assert.False(was.ContainsKey(spawned.Groups[1].Value), "spawned line for a new uid: " + line);
            }
            var died = Regex.Match(line, @"^(u\d+) \S+ died");
            if (died.Success && was.ContainsKey(died.Groups[1].Value))
            {
                Assert.False(now.ContainsKey(died.Groups[1].Value), "a died enemy is gone: " + line);
            }
        }
        // Every live enemy whose hp changed has an hp line (no silent kills or heals).
        foreach (var (uid, n) in now)
        {
            if (was.TryGetValue(uid, out var w2) && w2["hp"]!.GetValue<int>() != n["hp"]!.GetValue<int>())
            {
                Assert.True(log.Any(l => l.StartsWith(uid + " ", StringComparison.Ordinal) && l.Contains(" hp ", StringComparison.Ordinal)), $"hp change of {uid} is logged: " + string.Join(" | ", log));
            }
        }
        foreach (var uid in was.Keys)
        {
            if (!now.ContainsKey(uid))
            {
                Assert.True(log.Any(l => l.StartsWith(uid + " ", StringComparison.Ordinal) && (l.EndsWith(" died", StringComparison.Ordinal) || l.EndsWith(" escaped", StringComparison.Ordinal))), $"{uid} leaving is logged: " + string.Join(" | ", log));
            }
        }
    }

    public static void Test_CombatView_UidsStableAcrossKillAndIndicesCompact()
    {
        var s = NewService();
        var obs = Create(s, "SLIMES_NORMAL", 3, Enumerable.Repeat("STRIKE_IRONCLAD", 10).ToArray());
        var g = s.Find("c1")!;
        var cs = g.LiveCombat!;
        var enemies = obs["combat"]!["enemies"]!.AsArray();
        Assert.True(enemies.Count >= 3, "a multi-enemy fight");
        // uid is the kernel CombatId, the index the position in cs.Monsters.
        foreach (var e in enemies)
        {
            int index = e!["index"]!.GetValue<int>();
            Assert.Equal("u" + cs.Monsters[index].CombatId, e["uid"]!.GetValue<string>());
        }
        string victimUid = enemies[1]!["uid"]!.GetValue<string>();
        var victim = cs.Monsters[1];
        victim.CurrentHp = 1;
        victim.Block = 0;
        var before = Observe(s, "c1");
        var step = Step(s, "c1", PlayToken(before, "STRIKE_IRONCLAD", 1));
        var after = step["observation"]!.AsObject();
        var log = Log(step);
        Assert.True(log.Any(l => l.StartsWith(victimUid + " " + victim.MonsterId + " died", StringComparison.Ordinal)), "the kill logs 'died': " + string.Join(" | ", log));
        Assert.True(log[0].Contains($"({victimUid})", StringComparison.Ordinal), "the action desc names the target uid: " + log[0]);
        CheckNoGhosts(before, after, log);
        var nowByUid = EnemiesByUid(after);
        Assert.False(nowByUid.ContainsKey(victimUid), "the dead enemy is gone from the observation");
        foreach (var (uid, was) in EnemiesByUid(before))
        {
            if (uid == victimUid || !nowByUid.TryGetValue(uid, out var now))
            {
                continue;
            }
            Assert.Equal(was["id"]!.GetValue<string>(), now["id"]!.GetValue<string>(), "a uid keeps naming the same creature");
            int index = now["index"]!.GetValue<int>();
            Assert.Equal(uid, "u" + g.LiveCombat!.Monsters[index].CombatId, "index is the position in cs.Monsters after the kill");
        }
        // A normal enemy is removed from cs.Monsters on death: the enemies behind it move up one index.
        Assert.False(g.LiveCombat!.Monsters.Any(m => "u" + m.CombatId == victimUid), "the dead normal enemy left cs.Monsters");
        Assert.Equal(1, after["combat"]!["enemies"]!.AsArray().First(e => e!["uid"]!.GetValue<string>() == enemies[2]!["uid"]!.GetValue<string>())!["index"]!.GetValue<int>(), "the enemy behind the dead one compacts into its index");
    }

    public static void Test_CombatView_RevivableEnemyLeavesIndexGap()
    {
        var s = NewService();
        var obs = Create(s, "DECIMILLIPEDE_ELITE", 3, Enumerable.Repeat("STRIKE_IRONCLAD", 10).ToArray());
        var g = s.Find("c1")!;
        var cs = g.LiveCombat!;
        var first = cs.Monsters[0];
        Assert.True(first.HasPower("REATTACH_POWER"), "the segment can reattach");
        first.CurrentHp = 1;
        first.Block = 0;
        var before = Observe(s, "c1");
        var step = Step(s, "c1", PlayToken(before, "STRIKE_IRONCLAD", 0));
        var after = step["observation"]!.AsObject();
        var log = Log(step);
        string uid = "u" + first.CombatId;
        Assert.True(log.Any(l => l.StartsWith(uid + " " + first.MonsterId + " died", StringComparison.Ordinal)), "the segment's death is logged: " + string.Join(" | ", log));
        CheckNoGhosts(before, after, log);
        Assert.True(g.LiveCombat!.Monsters.Contains(first), "a revivable enemy stays in cs.Monsters when it dies");
        var indices = after["combat"]!["enemies"]!.AsArray().Select(e => e!["index"]!.GetValue<int>()).ToList();
        Assert.False(indices.Contains(0), "the dead segment's index is a gap");
        Assert.True(indices.Contains(1), "the others keep their indices");
    }

    public static void Test_CombatView_EnemyTurnNarration()
    {
        var s = NewService();
        Create(s, "NIBBITS_WEAK", 1);
        var step = Step(s, "c1", "end");
        var log = Log(step);
        int header = log.IndexOf("enemy turn:");
        Assert.True(header > 0, "an 'enemy turn:' block: " + string.Join(" | ", log));
        var move = log.Skip(header + 1).FirstOrDefault(l => Regex.IsMatch(l, @"^ u\d+ NIBBIT: \S+"));
        Assert.True(move != null, "a narrated NIBBIT move: " + string.Join(" | ", log));
        var hit = Regex.Match(move!, @"attack (\d+) x(\d+) -> player: (\d+) blocked, (\d+) dmg");
        Assert.True(hit.Success, "the move narrates its attack: " + move);
        var hpLine = log.First(l => l.StartsWith("hp ", StringComparison.Ordinal));
        var hp = Regex.Match(hpLine, @"^hp (\d+)/\d+ -> (\d+)/\d+ \((\d+) dmg, (\d+) blocked\)");
        Assert.True(hp.Success, "player hp line with the damage note: " + hpLine);
        Assert.Equal(int.Parse(hit.Groups[4].Value), int.Parse(hp.Groups[1].Value) - int.Parse(hp.Groups[2].Value), "narrated damage equals the hp lost");
        Assert.Equal(int.Parse(hit.Groups[1].Value) * int.Parse(hit.Groups[2].Value), int.Parse(hit.Groups[3].Value) + int.Parse(hit.Groups[4].Value), "hits = blocked + unblocked");
        Assert.True(log.Contains("turn 2"), "the next turn starts");
    }

    public static void Test_CombatView_EffectiveDamageMatchesKernelHit()
    {
        var s = NewService();
        Create(s, "SLIMES_NORMAL", 3, Enumerable.Repeat("STRIKE_IRONCLAD", 5).Concat(Enumerable.Repeat("PERFECTED_STRIKE", 5)).ToArray());
        var g = s.Find("c1")!;
        var cs = g.LiveCombat!;
        PowerCmd.Apply<StrengthPower>(cs, cs.Player, 3m, cs.Player, null);
        var target = cs.Monsters[1];
        PowerCmd.Apply<VulnerablePower>(cs, target, 2m, cs.Player, null);
        foreach (var m in cs.Monsters)
        {
            m.Block = 0;
            m.MaxHp = 500;
            m.CurrentHp = 500;
        }
        var obs = Observe(s, "c1");
        string uid = "u" + target.CombatId;
        string other = "u" + cs.Monsters[0].CombatId;
        int verified = 0;
        foreach (var cardId in new[] { "PERFECTED_STRIKE", "STRIKE_IRONCLAD" })
        {
            var hand = obs["combat"]!["hand"]!.AsArray();
            var card = hand.FirstOrDefault(c => c!["id"]!.GetValue<string>() == cardId);
            if (card == null)
            {
                continue;
            }
            var eff = card["effective"]!["damage"]!.AsObject();
            int expected = eff[uid]!.GetValue<int>();
            Assert.True(expected > eff[other]!.GetValue<int>(), $"{cardId}: Vulnerable raises the per-target number ({eff.ToJsonString()})");
            Assert.True(expected > card["vars"]!.AsObject().Select(kv => kv.Value!.GetValue<int>()).Max(), $"{cardId}: effective exceeds the base vars");
            int hpBefore = target.CurrentHp;
            Step(s, "c1", PlayToken(obs, cardId, cs.Monsters.IndexOf(target)));
            Assert.Equal(expected, hpBefore - target.CurrentHp, $"{cardId}: the kernel hit equals the effective number");
            Assert.True(obs["text"]!.GetValue<string>().Contains("(eff ", StringComparison.Ordinal), "the hand line shows the effective number");
            verified++;
            obs = Observe(s, "c1");
        }
        Assert.True(verified >= 1, "at least one card was checked against its kernel hit");
    }

    public static void Test_CombatView_DangerLineArithmetic()
    {
        var s = NewService();
        Create(s, "DECIMILLIPEDE_ELITE", 3);
        var g = s.Find("c1")!;
        g.LiveCombat!.Player.Block = 5;
        var obs = Observe(s, "c1");
        var danger = obs["combat"]!["danger"]!.AsObject();
        int sum = 0;
        int hits = 0;
        foreach (var e in obs["combat"]!["enemies"]!.AsArray())
        {
            foreach (var it in e!["intents"]!.AsArray())
            {
                if (it!["damage"] is JsonValue d)
                {
                    sum += d.GetValue<int>() * it["times"]!.GetValue<int>();
                    hits += it["times"]!.GetValue<int>();
                }
            }
        }
        Assert.True(sum > 0, "the opening has attacks");
        Assert.Equal(sum, danger["incoming"]!.GetValue<int>(), "incoming = sum of intent damage x times");
        Assert.Equal(hits, danger["hits"]!.GetValue<int>());
        Assert.Equal(5, danger["block"]!.GetValue<int>());
        int net = Math.Max(0, sum - 5);
        Assert.Equal(net, danger["net_hp_loss"]!.GetValue<int>());
        int hp = obs["player"]!["hp"]!.GetValue<int>();
        Assert.Equal(hp - net, danger["hp_after"]!.GetValue<int>());
        string text = obs["text"]!.GetValue<string>();
        Assert.True(text.Contains($"incoming: {sum} dmg in {hits} hits (", StringComparison.Ordinal) && text.Contains($"block 5, net -{net} HP -> {hp - net}/", StringComparison.Ordinal), "danger line in the text: " + text.Split('\n')[1]);
        // A timer power shows up as a '!' line: give the player Constrict.
        PowerCmd.Apply<ConstrictPower>(g.LiveCombat!, g.LiveCombat!.Player, 4m, g.LiveCombat!.Monsters[0], null);
        obs = Observe(s, "c1");
        Assert.True(obs["text"]!.GetValue<string>().Contains("! end of turn: CONSTRICT 4 dmg to player", StringComparison.Ordinal), "timer line for CONSTRICT");
        Assert.True(obs["combat"]!["danger"]!["timers"]!.AsArray().Any(t => t!["id"]!.GetValue<string>() == ConstrictPower.PowerId));
    }

    public static void Test_CombatView_PilesRoute()
    {
        var s = NewService();
        var obs = Create(s, "NIBBITS_WEAK", 1);
        var piles = s.Handle("GET", "/games/c1/piles", null);
        Assert.Equal(200, piles.Status, piles.Json);
        var body = piles.Body;
        Assert.True(body["in_combat"]!.GetValue<bool>());
        var draw = body["draw"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(obs["combat"]!["draw_count"]!.GetValue<int>(), draw.Count, "draw pile size");
        Assert.True(draw.SequenceEqual(draw.OrderBy(x => x, StringComparer.Ordinal)), "the draw pile is sorted (order hidden)");
        Assert.Equal(obs["player"]!["deck_size"]!.GetValue<int>(), body["deck"]!.AsArray().Count, "deck size");
        Assert.True(body["deck"]!.AsArray()[0]!.AsObject().ContainsKey("name"), "card nodes carry name/text");
        Assert.Equal(404, s.Handle("GET", "/games/zz/piles", null).Status);
    }

    public static void Test_CombatView_PotionPlayedCardsListed()
    {
        var s = NewService();
        Create(s, "NIBBITS_WEAK", 1);
        var g = s.Find("c1")!;
        Assert.True(g.Player.AddPotionInternal(PotionRegistry.Create("DISTILLED_CHAOS"), 0).Success);
        var step = Step(s, "c1", "potion 0");
        var log = Log(step);
        var line = log.FirstOrDefault(l => l.StartsWith("potion 0 DISTILLED_CHAOS: played ", StringComparison.Ordinal));
        Assert.True(line != null, "the cards the potion played are listed: " + string.Join(" | ", log));
        Assert.True(line!.Contains("(auto)", StringComparison.Ordinal), "auto-plays are marked: " + line);
    }

    public static void Test_CombatView_SurroundedFacingAndFlanks()
    {
        var s = NewService();
        var obs = Create(s, "KAISER_CRAB_BOSS", 1);
        Assert.True(obs["player"]!["facing"] is JsonValue, "the player has a facing in a SURROUNDED fight: " + obs["text"]);
        var enemies = obs["combat"]!["enemies"]!.AsArray();
        var flanks = enemies.Select(e => e!["flank"]?.GetValue<string>()).Where(f => f != null).ToList();
        Assert.True(flanks.Contains("left") && flanks.Contains("right"), "enemies stand on both sides: " + string.Join(",", flanks));
        Assert.Equal(1, enemies.Count(e => e!["behind"] is JsonValue b && b.GetValue<bool>()), "exactly one side is behind the player");
        string text = obs["text"]!.GetValue<string>();
        Assert.True(text.Contains("-- enemies (you face ", StringComparison.Ordinal) && text.Contains("(BEHIND you: x1.5)", StringComparison.Ordinal), "facing in the text");
        var surrounded = obs["player"]!["powers"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == SurroundedPower.PowerId)!;
        Assert.True(surrounded["detail"]!.GetValue<string>().StartsWith("facing ", StringComparison.Ordinal), "SURROUNDED detail names the facing");
    }

    // Random runs (every character): the step log never fails and every combat step's log is consistent with the
    // observations before and after it, by uid.
    public static void Test_CombatView_RandomRunsLogConsistent()
    {
        var s = NewService();
        var characters = new[] { "IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT" };
        int deaths = 0;
        int enemyTurns = 0;
        for (int r = 0; r < 10; r++)
        {
            var random = new System.Random(100 + r);
            var created = s.Handle("POST", "/runs", new JsonObject { ["seed"] = "VIEW" + r, ["character"] = characters[r % characters.Length] }.ToJsonString());
            Assert.Equal(200, created.Status, created.Json);
            string id = created.Body["id"]!.GetValue<string>();
            var obs = created.Body["observation"]!.AsObject();
            for (int n = 0; n < 1500 && !obs["terminal"]!.GetValue<bool>(); n++)
            {
                var tokens = obs["legal_actions"]!.AsArray().Select(a => a!["token"]!.GetValue<string>()).ToList();
                var plays = tokens.Where(t => t.StartsWith("play ", StringComparison.Ordinal)).ToList();
                var takes = tokens.Where(t => t is not ("skip" or "leave" or "proceed")).ToList();
                string token = plays.Count > 0 && random.Next(5) != 0 ? plays[random.Next(plays.Count)] : takes.Count > 0 && random.Next(4) != 0 ? takes[random.Next(takes.Count)] : tokens[random.Next(tokens.Count)];
                var step = Step(s, id, token);
                var log = Log(step);
                var after = step["observation"]!.AsObject();
                Assert.False(log.Any(l => l.StartsWith("(step log unavailable", StringComparison.Ordinal)), "step log failed: " + string.Join(" | ", log));
                bool lost = after["player"]!["hp"]!.GetValue<int>() == 0;
                if (obs["combat"] is JsonObject && obs["prompt"] == null && after["prompt"] == null && !lost)
                {
                    var afterView = after["combat"] is JsonObject ? after : new JsonObject { ["combat"] = new JsonObject { ["enemies"] = new JsonArray() } };
                    CheckNoGhosts(obs, afterView, log);
                }
                deaths += log.Count(l => Regex.IsMatch(l, @"^u\d+ \S+ died$"));
                enemyTurns += log.Count(l => l == "enemy turn:");
                obs = after;
            }
            s.Handle("DELETE", $"/games/{id}", null);
        }
        Assert.True(deaths > 10, $"kills were logged ({deaths})");
        Assert.True(enemyTurns > 10, $"enemy turns were narrated ({enemyTurns})");
    }

    public static void Test_CombatView_NewFieldsPresent()
    {
        var s = NewService();
        var obs = Create(s, "NIBBITS_WEAK", 1);
        var enemy = obs["combat"]!["enemies"]![0]!.AsObject();
        foreach (var key in new[] { "uid", "flank", "behind", "intents", "powers" })
        {
            Assert.True(enemy.ContainsKey(key), $"enemy node has '{key}'");
        }
        Assert.True(enemy["intents"]![0]!.AsObject().ContainsKey("move") && enemy["intents"]![0]!["move"] == null, "intent move id is present and null by default (reveal_moves off)");
        var card = obs["combat"]!["hand"]![0]!.AsObject();
        foreach (var key in new[] { "base_cost", "effective", "enchantments", "affliction", "dampened" })
        {
            Assert.True(card.ContainsKey(key), $"hand card has '{key}'");
        }
        Assert.True(obs["player"]!.AsObject().ContainsKey("facing"));
        Assert.True(obs["combat"]!.AsObject().ContainsKey("danger"));
        string text = obs["text"]!.GetValue<string>();
        Assert.True(Regex.IsMatch(text, @"\n \[0\] u\d+ NIBBIT "), "enemy line shows the uid");
        Assert.True(Regex.IsMatch(text, @"intent: Attack \d+"), "enemy line shows the intent without the move id: " + text);
    }
}
