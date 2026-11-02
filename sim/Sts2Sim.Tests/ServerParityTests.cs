using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Server;

namespace Sts2Sim.Tests;

// Wave E1 parity fixes: the observation shows what the game shows. An enemy's intent never names the move unless the
// game was created with "reveal_moves"; power amounts are the icon numbers and power details name no applier.
public static class ServerParityTests
{
    private static PlayService NewService(bool revealMoves = false)
        => new(new PlayServiceOptions { Version = "test", CrashLogPath = null, RevealMoves = revealMoves });

    private static JsonObject Post(PlayService s, string path, JsonObject body)
    {
        var r = s.Handle("POST", path, body.ToJsonString());
        Assert.Equal(200, r.Status, $"POST {path}: {r.Json}");
        return r.Body.AsObject();
    }

    private static JsonObject Step(PlayService s, string id, string token)
    {
        var r = s.Handle("POST", $"/games/{id}/step", new JsonObject { ["token"] = token }.ToJsonString());
        Assert.Equal(200, r.Status, $"step '{token}': {r.Json}");
        return r.Body.AsObject();
    }

    private static JsonObject Observe(PlayService s, string id) => s.Handle("GET", $"/games/{id}", null).Body["observation"]!.AsObject();

    private static List<string> Log(JsonObject step) => step["log"]!.AsArray().Select(l => l!.GetValue<string>()).ToList();

    // Every string value of a JSON tree: observation fields, the text, log lines.
    private static IEnumerable<string> Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, v) in o)
                {
                    foreach (var s in Strings(v))
                    {
                        yield return s;
                    }
                }
                break;
            case JsonArray a:
                foreach (var v in a)
                {
                    foreach (var s in Strings(v))
                    {
                        yield return s;
                    }
                }
                break;
            case JsonValue v when v.TryGetValue<string>(out var str):
                yield return str;
                break;
        }
    }

    // An UPPER_SNAKE id as a whole token, not inside a longer id (BITE in BITE_MOVE, STRIKE in STRIKE_IRONCLAD).
    private static bool HasToken(string s, string id) => Regex.IsMatch(s, "(?<![A-Z0-9_])" + Regex.Escape(id) + "(?![A-Z0-9_])");

    // The moves the game's enemies are about to perform (also dead-in-place ones).
    private static List<string> NextMoveIds(GameSession g)
        => g.LiveCombat?.Monsters.Where(m => m.HasAi && m.Ai.NextMove != null).Select(m => m.Ai.NextMove!.Id).ToList() ?? new List<string>();

    // Ids that legitimately appear as whole tokens in observations (cards, powers and their stems as in timer lines,
    // relics, potions, monsters, ...). A move id equal to one of them (CONSTRICT, next to the CONSTRICT timer line)
    // cannot be told apart by a text search, so it is not checked.
    private static HashSet<string> ContentIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in new[]
                 {
                     CardRegistry.AllIds, PowerRegistry.AllIds, RelicRegistry.AllIds, PotionRegistry.AllIds, MonsterRegistry.AllIds,
                     EncounterRegistry.AllIds, EventRegistry.AllIds, CharacterRegistry.AllIds, OrbRegistry.AllIds,
                     EnchantmentRegistry.AllIds, AfflictionRegistry.AllIds
                 })
        {
            foreach (var id in list)
            {
                ids.Add(id);
                if (id.EndsWith("_POWER", StringComparison.Ordinal))
                {
                    ids.Add(id[..^6]);
                }
            }
        }
        return ids;
    }

    private static void CheckIntentsHidden(JsonObject obs)
    {
        if (obs["combat"] is not JsonObject combat)
        {
            return;
        }
        foreach (var e in combat["enemies"]!.AsArray())
        {
            foreach (var it in e!["intents"]!.AsArray())
            {
                Assert.True(it!.AsObject().ContainsKey("move") && it["move"] == null && it["move_name"] == null, "intent move and move_name are null: " + it.ToJsonString());
            }
        }
        Assert.False(obs["text"]!.GetValue<string>().Contains("intent: [", StringComparison.Ordinal), "the text shows no move id");
    }

    // Default (reveal off): over random runs of every character, no string of any observation or step log contains the
    // id of a move an enemy is about to perform (before the step: the moves the enemy turn performs; after: the next ones).
    public static void Test_Parity_MovesHiddenByDefault()
    {
        var s = NewService();
        var content = ContentIds();
        var characters = new[] { "IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT" };
        int checkedIds = 0;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (int r = 0; r < characters.Length; r++)
        {
            var random = new System.Random(700 + r);
            var created = Post(s, "/runs", new JsonObject { ["seed"] = "PARITY" + r, ["character"] = characters[r] });
            string id = created["id"]!.GetValue<string>();
            var g = s.Find(id)!;
            Assert.False(g.RevealMoves, "reveal_moves defaults to off");
            Assert.False(s.Handle("GET", $"/games/{id}/history", null).Body["config"]!["reveal_moves"]!.GetValue<bool>(), "the config records reveal_moves");
            var obs = created["observation"]!.AsObject();
            for (int n = 0; n < 700 && !obs["terminal"]!.GetValue<bool>(); n++)
            {
                CheckIntentsHidden(obs);
                var moves = NextMoveIds(g);
                var tokens = obs["legal_actions"]!.AsArray().Select(a => a!["token"]!.GetValue<string>()).ToList();
                var plays = tokens.Where(t => t.StartsWith("play ", StringComparison.Ordinal)).ToList();
                var takes = tokens.Where(t => t is not ("skip" or "leave" or "proceed")).ToList();
                string token = plays.Count > 0 && random.Next(5) != 0 ? plays[random.Next(plays.Count)] : takes.Count > 0 && random.Next(4) != 0 ? takes[random.Next(takes.Count)] : tokens[random.Next(tokens.Count)];
                var step = Step(s, id, token);
                moves.AddRange(NextMoveIds(g));
                var strings = Strings(step).ToList();
                foreach (var move in moves.Distinct().Where(m => !content.Contains(m)))
                {
                    var leak = strings.FirstOrDefault(x => HasToken(x, move));
                    Assert.True(leak == null, $"{id} step {n} '{token}': move id {move} leaked in: {leak}");
                    checkedIds++;
                    distinct.Add(move);
                }
                obs = step["observation"]!.AsObject();
            }
            s.Handle("DELETE", $"/games/{id}", null);
        }
        Assert.True(checkedIds > 200 && distinct.Count > 15, $"enough enemy moves were checked ({checkedIds} checks, {distinct.Count} distinct moves)");
    }

    // reveal_moves on (per game or as the server default): the intent carries the NextMove id and title, the text and
    // the enemy-turn log name it; off per game overrides an on server default.
    public static void Test_Parity_RevealMovesOption()
    {
        var s = NewService();
        var obs = Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK", ["reveal_moves"] = true })["observation"]!.AsObject();
        var g = s.Find("c1")!;
        Assert.True(g.RevealMoves);
        Assert.True(s.Handle("GET", "/games/c1/history", null).Body["config"]!["reveal_moves"]!.GetValue<bool>(), "the config records reveal_moves");
        var monster = g.LiveCombat!.Monsters[0];
        string move = monster.Ai.NextMove!.Id;
        var intent = obs["combat"]!["enemies"]![0]!["intents"]![0]!;
        Assert.Equal(move, intent["move"]!.GetValue<string>(), "the intent carries the NextMove id");
        if (RulesText.Loaded)
        {
            Assert.True(intent["move_name"] is JsonValue, "and its title once rules text is loaded");
        }
        Assert.True(Strings(obs).Any(x => HasToken(x, move)), "the observation JSON contains the NextMove id");
        Assert.True(obs["text"]!.GetValue<string>().Contains("intent: [" + move, StringComparison.Ordinal), "the text shows the move id");
        var log = Log(Step(s, "c1", "end"));
        Assert.True(log.Any(l => l.StartsWith($" u{monster.CombatId} NIBBIT: {move}", StringComparison.Ordinal)), "the enemy-turn line leads with the move id: " + string.Join(" | ", log));

        var on = NewService(revealMoves: true);
        var defaulted = Post(on, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" })["observation"]!.AsObject();
        Assert.Equal(move, defaulted["combat"]!["enemies"]![0]!["intents"]![0]!["move"]!.GetValue<string>(), "--reveal-moves makes it the default");
        var hidden = Post(on, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK", ["reveal_moves"] = false })["observation"]!.AsObject();
        CheckIntentsHidden(hidden);
        var run = Post(on, "/runs", new JsonObject { ["seed"] = "REVEAL1" });
        Assert.True(on.Find(run["id"]!.GetValue<string>())!.RevealMoves, "runs take the server default too");
        Assert.Equal(400, s.Handle("POST", "/runs", new JsonObject { ["reveal_moves"] = "yes" }.ToJsonString()).Status, "reveal_moves must be a bool");
    }

    // Reveal off: an enemy-turn line is the move's effects; a move without visible effects shows its intent types.
    public static void Test_Parity_EnemyTurnNarrationWithoutMoveIds()
    {
        var s = NewService();
        Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" });
        var g = s.Find("c1")!;
        var monster = g.LiveCombat!.Monsters[0];
        string move = monster.Ai.NextMove!.Id;
        var log = Log(Step(s, "c1", "end"));
        string prefix = $" u{monster.CombatId} NIBBIT: ";
        var line = log.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(line != null && Regex.IsMatch(line[prefix.Length..], @"^attack \d+ x\d+ -> player: \d+ blocked, \d+ dmg"), "the line starts with the attack: " + string.Join(" | ", log));
        Assert.False(log.Any(l => HasToken(l, move)), "no log line names the move " + move);

        var cs = g.LiveCombat!;
        CreatureCmd.Stun(cs, cs.Monsters[0]);
        var obs = Observe(s, "c1");
        Assert.True(obs["text"]!.GetValue<string>().Contains("intent: Stun", StringComparison.Ordinal), "a stunned enemy's intent is Stun: " + obs["text"]);
        log = Log(Step(s, "c1", "end"));
        Assert.True(log.Contains(prefix + "Stun"), "an effect-free move shows the intent the player saw: " + string.Join(" | ", log));
    }

    // Power nodes: amount is the icon number (SimProjector.DisplayAmount), text keeps the kernel amount, amount_raw only
    // where the projection has no display rule, and detail names no applier.
    public static void Test_Parity_PowerAmountsAndDetail()
    {
        var s = NewService();
        Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK", ["hp"] = 500, ["max_hp"] = 500 });
        var g = s.Find("c1")!;
        var cs = g.LiveCombat!;
        var enemy = cs.Monsters[0];
        enemy.MaxHp = 500;
        enemy.CurrentHp = 500;
        enemy.Block = 0;
        PowerCmd.Apply<WeakPower>(cs, cs.Player, 2m, enemy, null);
        PowerCmd.Apply<TenderPower>(cs, cs.Player, 1m, enemy, null);
        PowerCmd.Apply<SlothPower>(cs, cs.Player, 3m, enemy, null);
        PowerCmd.Apply<PoisonPower>(cs, enemy, 5m, cs.Player, null);
        PowerCmd.Apply<SlowPower>(cs, enemy, 1m, cs.Player, null);
        PowerCmd.Apply<HardenedShellPower>(cs, enemy, 20m, enemy, null);
        JsonObject PlayerPower(JsonObject o, string id) => o["player"]!["powers"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject EnemyPower(JsonObject o, string id) => o["combat"]!["enemies"]![0]!["powers"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == id)!.AsObject();

        var obs = Observe(s, "c1");
        Assert.Null(PlayerPower(obs, WeakPower.PowerId)["detail"], "WEAK applied by the enemy has no 'from' detail");
        Assert.Equal("0 cards played this turn", PlayerPower(obs, TenderPower.PowerId)["detail"]!.GetValue<string>(), "TENDER's detail is its counter only");
        Assert.Null(EnemyPower(obs, PoisonPower.PowerId)["detail"], "POISON applied by the player has no 'from' detail");
        Assert.False(obs["text"]!.GetValue<string>().Contains("[from ", StringComparison.Ordinal), "no applier in the text");
        Assert.Equal(0, PlayerPower(obs, TenderPower.PowerId)["amount"]!.GetValue<int>(), "TENDER shows the cards played this turn");
        Assert.Equal(0, EnemyPower(obs, SlowPower.PowerId)["amount"]!.GetValue<int>(), "SLOW shows its bonus percent");
        Assert.Equal(20, EnemyPower(obs, HardenedShellPower.PowerId)["amount"]!.GetValue<int>(), "HARDENED_SHELL shows the HP it can still lose");
        var sloth = PlayerPower(obs, SlothPower.PowerId);
        Assert.Equal(3, sloth["amount"]!.GetValue<int>(), "SLOTH has no display rule yet: raw amount");
        Assert.Equal(3, sloth["amount_raw"]!.GetValue<int>(), "and says so with amount_raw");
        Assert.Equal("0/3 cards played this turn", sloth["detail"]!.GetValue<string>());
        foreach (var p in obs["player"]!["powers"]!.AsArray().Concat(obs["combat"]!["enemies"]![0]!["powers"]!.AsArray()))
        {
            Assert.Equal(p!["id"]!.GetValue<string>() == SlothPower.PowerId, p.AsObject().ContainsKey("amount_raw"), "amount_raw only on a power without a display rule: " + p.ToJsonString());
        }

        var hand = obs["combat"]!["hand"]!.AsArray();
        int strike = hand.Select(c => c!["id"]!.GetValue<string>()).ToList().IndexOf("STRIKE_IRONCLAD");
        Assert.True(strike >= 0, "a Strike in hand");
        var step = Step(s, "c1", $"play {strike} -> 0");
        obs = step["observation"]!.AsObject();
        int lost = 500 - enemy.CurrentHp;
        Assert.True(lost > 0, "the Strike hit");
        Assert.Equal(1, PlayerPower(obs, TenderPower.PowerId)["amount"]!.GetValue<int>(), "TENDER counts the card");
        Assert.Equal("1 cards played this turn", PlayerPower(obs, TenderPower.PowerId)["detail"]!.GetValue<string>());
        Assert.Equal(10, EnemyPower(obs, SlowPower.PowerId)["amount"]!.GetValue<int>(), "SLOW: +10% per card");
        Assert.Equal(20 - lost, EnemyPower(obs, HardenedShellPower.PowerId)["amount"]!.GetValue<int>(), "HARDENED_SHELL: the cap minus the damage taken this turn");
        if (RulesText.Loaded)
        {
            Assert.True(EnemyPower(obs, HardenedShellPower.PowerId)["text"]!.GetValue<string>().Contains("20", StringComparison.Ordinal), "the description keeps the kernel amount");
        }
        string text = obs["text"]!.GetValue<string>();
        Assert.True(text.Contains("TENDER_POWER 1 [1 cards played this turn]", StringComparison.Ordinal) && text.Contains($"HARDENED_SHELL_POWER {20 - lost}", StringComparison.Ordinal), "the text shows the icon numbers: " + text);
        var log = Log(step);
        Assert.True(log.Contains("player TENDER_POWER 0 -> 1"), "the step log diffs the icon numbers: " + string.Join(" | ", log));
    }

    // CHAINS_OF_BINDING's detail lists the cards it bound (each also marked [BOUND] in the hand).
    public static void Test_Parity_ChainsOfBindingDetailListsBoundCards()
    {
        var s = NewService();
        Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" });
        var cs = s.Find("c1")!.LiveCombat!;
        PowerCmd.Apply<ChainsOfBindingPower>(cs, cs.Player, 2m, cs.Monsters[0], null);
        var bound = cs.Player.Hand.Cards.Take(2).ToList();
        CardCmd.AfflictAndPreview(cs, bound, ChainsOfBindingPower.BoundAfflictionId, 2m);
        var obs = Observe(s, "c1");
        var chains = obs["player"]!["powers"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == ChainsOfBindingPower.PowerId)!;
        Assert.Equal($"2 BOUND cards ({bound[0].Id}, {bound[1].Id}); a BOUND card was played this turn: no", chains["detail"]!.GetValue<string>());
        Assert.Equal(2, obs["text"]!.GetValue<string>().Split('\n').Count(l => l.Contains(" [BOUND", StringComparison.Ordinal)), "the hand marks the bound cards");
    }
}
