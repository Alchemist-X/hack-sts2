using System.Text.Json.Nodes;
using Sts2Sim.Server;

namespace Sts2Sim.Tests;

public static class ServerTests
{
    private static PlayService NewService() => new(new PlayServiceOptions { Version = "test", CrashLogPath = null });

    private static JsonObject Post(PlayService s, string path, JsonObject body, int expected = 200)
    {
        var r = s.Handle("POST", path, body.ToJsonString());
        Assert.Equal(expected, r.Status, $"POST {path}: {r.Json}");
        return r.Body.AsObject();
    }

    private static JsonObject StepOk(PlayService s, string id, string token)
        => Post(s, $"/games/{id}/step", new JsonObject { ["token"] = token });

    private static List<string> Tokens(JsonObject obs)
        => obs["legal_actions"]!.AsArray().Select(a => a!["token"]!.GetValue<string>()).ToList();

    private static void CheckObservation(JsonObject obs)
    {
        foreach (var key in new[] { "game_id", "kind", "phase", "terminal", "result", "act", "floor", "turn", "player", "combat", "map", "rewards", "card_reward", "shop", "rest", "treasure", "event", "prompt", "legal_actions", "text" })
        {
            Assert.True(obs.ContainsKey(key), $"observation lacks '{key}'");
        }
        var tokens = Tokens(obs);
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count(), "legal tokens are unique: " + string.Join(" | ", tokens));
        Assert.Equal(obs["terminal"]!.GetValue<bool>(), tokens.Count == 0, "only a terminal observation has no legal action");
        string text = obs["text"]!.GetValue<string>();
        foreach (var t in tokens)
        {
            Assert.True(text.Contains(" " + t + "  : ", StringComparison.Ordinal), $"text lists token '{t}'");
        }
    }

    public static void Test_Server_HealthAndCatalog()
    {
        var s = NewService();
        var health = s.Handle("GET", "/health", null);
        Assert.Equal(200, health.Status);
        Assert.True(health.Body["ok"]!.GetValue<bool>());
        Assert.Equal("test", health.Body["version"]!.GetValue<string>());
        var catalog = s.Handle("GET", "/catalog", null).Body;
        Assert.True(catalog["characters"]!.AsArray().Any(c => c!.GetValue<string>() == "IRONCLAD"));
        Assert.True(catalog["encounters"]!.AsArray().Any(c => c!.GetValue<string>() == "NIBBITS_WEAK"));
        Assert.True(catalog["cards"]!.AsArray().Any(c => c!.GetValue<string>() == "STRIKE_IRONCLAD"));
        Assert.Equal(404, s.Handle("GET", "/nope", null).Status);
        Assert.Equal(404, s.Handle("GET", "/games/r99", null).Status);
    }

    public static void Test_Server_NewRunObservation()
    {
        var s = NewService();
        var created = Post(s, "/runs", new JsonObject { ["seed"] = "ABC123", ["character"] = "IRONCLAD", ["ascension"] = 0, ["content"] = "ported_only" });
        Assert.Equal("r1", created["id"]!.GetValue<string>());
        var obs = created["observation"]!.AsObject();
        CheckObservation(obs);
        Assert.Equal("run", obs["kind"]!.GetValue<string>());
        Assert.Equal(1, obs["act"]!.GetValue<int>());
        Assert.Equal("IRONCLAD", obs["player"]!["character"]!.GetValue<string>());
        Assert.Equal(10, obs["player"]!["deck_size"]!.GetValue<int>());
        Assert.Equal(80, obs["player"]!["max_hp"]!.GetValue<int>());
        var again = Post(s, "/runs", new JsonObject { ["seed"] = "ABC123" })["observation"]!.AsObject();
        Assert.Equal(obs["text"]!.GetValue<string>().Replace("r1", "r2"), again["text"]!.GetValue<string>(), "same seed, same opening");
        Assert.Equal(2, s.GameCount);
        Assert.Equal(200, s.Handle("DELETE", "/games/r1", null).Status);
        Assert.Equal(404, s.Handle("DELETE", "/games/r1", null).Status);
        Assert.Equal(1, s.GameCount);
    }

    public static void Test_Server_BadRequests()
    {
        var s = NewService();
        Post(s, "/runs", new JsonObject { ["character"] = "NOBODY" }, 400);
        Post(s, "/runs", new JsonObject { ["content"] = "everything" }, 400);
        Post(s, "/combats", new JsonObject { ["seed"] = 1 }, 400);
        Post(s, "/combats", new JsonObject { ["encounter"] = "NOT_AN_ENCOUNTER" }, 400);
        Post(s, "/combats", new JsonObject { ["encounter"] = "NIBBITS_WEAK", ["deck"] = new JsonArray("NOT_A_CARD") }, 400);
        Assert.Equal(400, s.Handle("POST", "/runs", "{not json").Status);
        Post(s, "/runs", new JsonObject { ["seed"] = 42 });
        Assert.Equal(400, s.Handle("POST", "/games/r1/step", "{}").Status);
    }

    public static void Test_Server_IllegalTokenLeavesGameUnchanged()
    {
        var s = NewService();
        Post(s, "/runs", new JsonObject { ["seed"] = "ILLEGAL1" });
        var g = s.Find("r1")!;
        ulong before = g.StateHash();
        foreach (var bad in new[] { "bogus", "play 99", "travel 42", "pick 7", "" })
        {
            var r = s.Handle("POST", "/games/r1/step", new JsonObject { ["token"] = bad }.ToJsonString());
            Assert.Equal(400, r.Status, $"'{bad}' is rejected");
            Assert.False(r.Body["ok"]!.GetValue<bool>());
            Assert.True(r.Body["legal_actions"]!.AsArray().Count > 0, "the 400 lists the legal actions");
            Assert.Equal(before, g.StateHash(), $"'{bad}' leaves the state unchanged");
        }
        Assert.Equal(0, g.Steps, "illegal tokens are not recorded");
    }

    public static void Test_Server_RandomRunThroughTokens()
    {
        var s = NewService();
        var random = new System.Random(5);
        var obs = Post(s, "/runs", new JsonObject { ["seed"] = "TOKENRUN" })["observation"]!.AsObject();
        int steps = 0;
        var phases = new HashSet<string>(StringComparer.Ordinal);
        while (!obs["terminal"]!.GetValue<bool>() && steps < 6000)
        {
            CheckObservation(obs);
            phases.Add(obs["phase"]!.GetValue<string>());
            var tokens = Tokens(obs);
            var step = StepOk(s, "r1", tokens[random.Next(tokens.Count)]);
            Assert.True(step["ok"]!.GetValue<bool>());
            Assert.True(step["log"]!.AsArray().Count >= 1, "a step returns its log");
            obs = step["observation"]!.AsObject();
            steps++;
        }
        CheckObservation(obs);
        Assert.True(obs["terminal"]!.GetValue<bool>(), "the random run ends");
        Assert.True(obs["result"]!.GetValue<string>() is "win" or "loss");
        Assert.True(phases.Contains("Map") && phases.Contains("Combat"), "the run visits the map and a combat: " + string.Join(",", phases));
        var history = s.Handle("GET", "/games/r1/history", null).Body;
        Assert.Equal(steps, history["steps"]!.GetValue<int>());
        Assert.Equal(steps, history["tokens"]!.AsArray().Count);
        Assert.False(history["crashed"]!.GetValue<bool>());
        Assert.Equal("TOKENRUN", history["seed"]!.GetValue<string>());

        var replay = Replay.Run(new JsonObject { ["kind"] = "run", ["config"] = history["config"]!.DeepClone(), ["tokens"] = history["tokens"]!.DeepClone() });
        Assert.False(replay.Reproduced, "a clean history replays cleanly: " + replay.Error);
        Assert.Equal(steps, replay.StepsApplied);
        Assert.Null(replay.Error);
    }

    public static void Test_Server_PromptTokens()
    {
        var s = NewService();
        for (int seed = 1; seed <= 40; seed++)
        {
            var random = new System.Random(seed);
            var created = Post(s, "/runs", new JsonObject { ["seed"] = "PROMPT" + seed });
            string id = created["id"]!.GetValue<string>();
            var obs = created["observation"]!.AsObject();
            for (int n = 0; n < 400 && !obs["terminal"]!.GetValue<bool>(); n++)
            {
                if (obs["prompt"] is JsonObject prompt)
                {
                    var tokens = Tokens(obs);
                    Assert.True(tokens.All(t => t.StartsWith("pick ", StringComparison.Ordinal) || t is "confirm" or "cancel"), "a prompt offers only pick/confirm/cancel: " + string.Join(" | ", tokens));
                    Assert.True(prompt["options"]!.AsArray().Count > 0);
                    Assert.Equal(tokens.Contains("confirm"), prompt["can_confirm"]!.GetValue<bool>(), "can_confirm matches the confirm token");
                    Assert.Equal(tokens.Contains("cancel"), prompt["can_cancel"]!.GetValue<bool>(), "can_cancel matches the cancel token");
                    Post(s, $"/games/{id}/step", new JsonObject { ["token"] = "end" }, 400);
                    var pick = tokens.FirstOrDefault(t => t.StartsWith("pick ", StringComparison.Ordinal)) ?? tokens[0];
                    var after = StepOk(s, id, pick)["observation"]!.AsObject();
                    CheckObservation(after);
                    if (pick.StartsWith("pick ", StringComparison.Ordinal) && after["prompt"] is JsonObject still && still["site"]!.GetValue<string>() == prompt["site"]!.GetValue<string>())
                    {
                        Assert.True(still["picked"]!.AsArray().Count >= 1, "a non-final pick is recorded in 'picked'");
                    }
                    return;
                }
                var legal = Tokens(obs);
                obs = StepOk(s, id, legal[random.Next(legal.Count)])["observation"]!.AsObject();
            }
            s.Handle("DELETE", $"/games/{id}", null);
        }
        Assert.Fail("no prompt met in 40 random runs");
    }

    public static void Test_Server_TokenNormalisationAndAliases()
    {
        var s = NewService();
        var obs = Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" })["observation"]!.AsObject();
        var attack = Tokens(obs).First(t => t.Contains("->", StringComparison.Ordinal));
        var parts = attack.Split(' ');
        var step = StepOk(s, "c1", $"  PLAY {parts[1]}->{parts[3]} ");
        Assert.Equal(attack, s.Handle("GET", "/games/c1/history", null).Body["tokens"]![0]!.GetValue<string>(), "history stores the canonical token");
        Assert.True(step["log"]!.AsArray().Any(l => l!.GetValue<string>().Contains("hp", StringComparison.Ordinal)), "the log reports the damage");
        StepOk(s, "c1", "end turn");
    }

    public static void Test_Server_CombatGame()
    {
        var s = NewService();
        var created = Post(s, "/combats", new JsonObject
        {
            ["seed"] = 7,
            ["deck"] = new JsonArray("STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "BASH"),
            ["encounter"] = "NIBBITS_WEAK",
            ["ascension"] = 0,
            ["hp"] = 60,
            ["max_hp"] = 80
        });
        Assert.Equal("c1", created["id"]!.GetValue<string>());
        var obs = created["observation"]!.AsObject();
        CheckObservation(obs);
        Assert.Equal("combat", obs["kind"]!.GetValue<string>());
        Assert.Equal("Combat", obs["phase"]!.GetValue<string>());
        Assert.Equal(60, obs["player"]!["hp"]!.GetValue<int>());
        var combat = obs["combat"]!.AsObject();
        Assert.Equal(5, combat["hand"]!.AsArray().Count);
        var enemy = combat["enemies"]!.AsArray()[0]!;
        Assert.Equal("NIBBIT", enemy["id"]!.GetValue<string>());
        Assert.True(enemy["intents"]!.AsArray().Count > 0, "enemy intents are shown");
        Assert.True(combat["hand"]!.AsArray().All(c => c!["vars"]!.AsObject().Count > 0), "starter cards expose their vars");
        var random = new System.Random(3);
        int steps = 0;
        while (!obs["terminal"]!.GetValue<bool>() && steps < 2000)
        {
            var tokens = Tokens(obs);
            var plays = tokens.Where(t => t.StartsWith("play", StringComparison.Ordinal)).ToList();
            string token = plays.Count > 0 && random.Next(4) != 0 ? plays[random.Next(plays.Count)] : tokens[random.Next(tokens.Count)];
            obs = StepOk(s, "c1", token)["observation"]!.AsObject();
            CheckObservation(obs);
            steps++;
        }
        Assert.True(obs["terminal"]!.GetValue<bool>(), "the combat ends");
        Assert.True(obs["phase"]!.GetValue<string>() is "Victory" or "Defeat");
        Assert.Equal(obs["phase"]!.GetValue<string>() == "Victory" ? "win" : "loss", obs["result"]!.GetValue<string>());
        Post(s, "/games/c1/step", new JsonObject { ["token"] = "end" }, 400);
    }

    public static void Test_Server_CrashIsLoggedWithRepro()
    {
        string log = Path.Combine(Path.GetTempPath(), $"sts2sim-crash-{Guid.NewGuid():N}.jsonl");
        int calls = 0;
        var s = new PlayService(new PlayServiceOptions
        {
            Version = "test",
            CrashLogPath = log,
            BeforeStep = (_, _) =>
            {
                if (++calls == 2)
                {
                    throw new InvalidOperationException("injected");
                }
            }
        });
        try
        {
            var obs = Post(s, "/combats", new JsonObject { ["seed"] = 3, ["encounter"] = "NIBBITS_WEAK" })["observation"]!.AsObject();
            string first = Tokens(obs)[0];
            StepOk(s, "c1", first);
            var crash = s.Handle("POST", "/games/c1/step", new JsonObject { ["token"] = "end" }.ToJsonString());
            Assert.Equal(500, crash.Status);
            Assert.True(crash.Body["crashed"]!.GetValue<bool>());
            Assert.Equal("InvalidOperationException: injected", crash.Body["error"]!.GetValue<string>());
            Assert.Equal(409, s.Handle("POST", "/games/c1/step", new JsonObject { ["token"] = "end" }.ToJsonString()).Status, "a crashed game refuses further steps");
            var history = s.Handle("GET", "/games/c1/history", null).Body;
            Assert.True(history["crashed"]!.GetValue<bool>());
            Assert.Equal("end", history["crash_token"]!.GetValue<string>());
            var lines = File.ReadAllLines(log);
            Assert.Equal(1, lines.Length);
            var repro = JsonNode.Parse(lines[0])!.AsObject();
            Assert.Equal("combat", repro["kind"]!.GetValue<string>());
            Assert.Equal(3, repro["seed"]!.GetValue<int>());
            Assert.Equal("NIBBITS_WEAK", repro["config"]!["encounter"]!.GetValue<string>());
            Assert.SequenceEqual(new[] { first, "end" }, repro["tokens"]!.AsArray().Select(t => t!.GetValue<string>()));
            var replay = Replay.Run(repro);
            Assert.False(replay.Reproduced, "the injected fault is not in the simulator");
            Assert.Equal(2, replay.StepsApplied);
        }
        finally
        {
            File.Delete(log);
        }
    }

    public static void Test_Server_SelfTestSmall()
    {
        var s = NewService();
        var result = SelfTest.Run(s, new SelfTestOptions { Runs = 6, FirstSeed = 100 });
        Assert.Equal(0, result.ServerErrors, string.Join("\n", result.ServerErrorMessages));
        Assert.Equal(6, result.Completed + result.Crashed + result.Capped);
        Assert.True(result.Steps > 0);
        Assert.Equal(0, s.GameCount, "selftest deletes its games");
    }

    public static void Test_Server_RulesTextFieldsPresent()
    {
        var s = NewService();
        var obs = Post(s, "/combats", new JsonObject { ["seed"] = 1, ["encounter"] = "NIBBITS_WEAK" })["observation"]!.AsObject();
        CheckObservation(obs);
        var card = obs["combat"]!["hand"]!.AsArray()[0]!.AsObject();
        Assert.True(card.ContainsKey("name") && card.ContainsKey("text"), "hand card has name/text");
        var enemy = obs["combat"]!["enemies"]!.AsArray()[0]!.AsObject();
        Assert.True(enemy.ContainsKey("name"), "enemy has name");
        foreach (var intent in enemy["intents"]!.AsArray())
        {
            Assert.True(intent!.AsObject().ContainsKey("move"), "intent has move");
        }
        foreach (var relic in obs["player"]!["relics"]!.AsArray())
        {
            Assert.True(relic!.AsObject().ContainsKey("name") && relic.AsObject().ContainsKey("text"), "relic has name/text");
        }
        if (!RulesText.Loaded)
        {
            Assert.True(card["name"] == null && card["text"] == null, "null until rules text is loaded");
            Assert.False(obs["text"]!.GetValue<string>().Contains('\u2014'), "no rules text rendered while unloaded");
        }
    }
}
