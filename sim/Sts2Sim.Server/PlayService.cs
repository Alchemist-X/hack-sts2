using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;

namespace Sts2Sim.Server;

public sealed record ServiceResponse(int Status, JsonNode Body)
{
    public string Json => Body.ToJsonString(PlayService.JsonOptions);
}

public sealed record PlayServiceOptions
{
    public string Version { get; init; } = "unknown";

    public string? CrashLogPath { get; init; } = "tmp/play-crashes.jsonl";

    public int MaxGames { get; init; } = 10_000;

    // Test seam: runs inside the crash guard just before each simulator step.
    public Action<GameSession, RunAction>? BeforeStep { get; init; }
}

// The HTTP API of sim/PLAY.md as plain in-process handlers; HttpHost and SelfTest both call Handle.
public sealed class PlayService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly object CrashLogGate = new();
    private const string SeedAlphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private readonly ConcurrentDictionary<string, GameSession> _games = new(StringComparer.Ordinal);
    private int _runCounter;
    private int _combatCounter;

    public PlayService(PlayServiceOptions? options = null)
    {
        Options = options ?? new PlayServiceOptions();
    }

    public PlayServiceOptions Options { get; }

    public int GameCount => _games.Count;

    public int CrashCount;

    public ServiceResponse Handle(string method, string path, string? body)
    {
        try
        {
            string clean = path.Split('?', 2)[0].TrimEnd('/');
            if (clean.Length == 0)
            {
                clean = "/";
            }
            var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
            switch (method.ToUpperInvariant())
            {
                case "GET" when clean == "/health":
                    return Health();
                case "GET" when clean == "/catalog":
                    return Catalog(path.Contains('?') ? path.Split('?', 2)[1] : null);
                case "POST" when clean == "/runs":
                    return CreateRun(ParseBody(body));
                case "POST" when clean == "/combats":
                    return CreateCombat(ParseBody(body));
                case "GET" when parts.Length == 2 && parts[0] == "games":
                    return GetGame(parts[1]);
                case "GET" when parts.Length == 3 && parts[0] == "games" && parts[2] == "history":
                    return History(parts[1]);
                case "GET" when parts.Length == 3 && parts[0] == "games" && parts[2] == "piles":
                    return Piles(parts[1]);
                case "POST" when parts.Length == 3 && parts[0] == "games" && parts[2] == "step":
                {
                    var obj = ParseBody(body);
                    string? token = obj["token"]?.GetValueKind() == JsonValueKind.String ? obj["token"]!.GetValue<string>() : null;
                    if (token == null)
                    {
                        return Error(400, "body must be {\"token\": \"...\"}");
                    }
                    return Step(parts[1], token);
                }
                case "DELETE" when parts.Length == 2 && parts[0] == "games":
                    return Delete(parts[1]);
                default:
                    return Error(404, $"no route {method} {path}");
            }
        }
        catch (BadRequestException ex)
        {
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(500, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public ServiceResponse Health()
        => new(200, new JsonObject { ["ok"] = true, ["version"] = Options.Version, ["games"] = _games.Count });

    // GET /catalog?kind=cards|relics|potions|powers|events|enemies|keywords[&ids=A,B][&grep=substr]
    // -> {"kind","items":[{id,name,text}]} (enemies: + moves[{id,title}]); without kind: the id lists below.
    public ServiceResponse Catalog(string? query)
    {
        var args = CatalogQuery(query);
        if (!args.TryGetValue("kind", out var kind) || kind.Length == 0)
        {
            return Catalog();
        }
        kind = kind.ToLowerInvariant();
        var items = RulesText.Catalog(kind);
        if (items == null)
        {
            return Error(400, $"unknown catalog kind '{kind}'; use one of {string.Join(", ", RulesText.Kinds)}");
        }
        HashSet<string>? ids = args.TryGetValue("ids", out var idList) && idList.Length > 0
            ? new HashSet<string>(idList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase)
            : null;
        string? grep = args.TryGetValue("grep", out var g) && g.Length > 0 ? g : null;
        var list = new JsonArray();
        foreach (var item in items)
        {
            if (ids != null && !ids.Contains(item.Id) && !(kind == "powers" && ids.Contains(item.Id.EndsWith("_POWER", StringComparison.Ordinal) ? item.Id[..^6] : item.Id)))
            {
                continue;
            }
            if (grep != null
                && item.Id.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0
                && (item.Name == null || item.Name.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0)
                && (item.Text == null || item.Text.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }
            list.Add(CatalogItemNode(kind, item));
        }
        return new(200, new JsonObject { ["kind"] = kind, ["loaded"] = RulesText.Loaded, ["items"] = list });
    }

    public static JsonObject CatalogItemNode(string kind, RulesText.CatalogItem item)
    {
        var node = new JsonObject { ["id"] = item.Id, ["name"] = item.Name, ["text"] = item.Text };
        if (kind == "enemies")
        {
            var moves = new JsonArray();
            if (item.Moves != null)
            {
                foreach (var (id, title) in item.Moves.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    moves.Add(new JsonObject { ["id"] = id, ["title"] = title });
                }
            }
            node["moves"] = moves;
        }
        return node;
    }

    private static Dictionary<string, string> CatalogQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            string value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }
        return result;
    }

    public ServiceResponse Catalog()
    {
        static JsonArray Ids(IEnumerable<string> ids) => new(ids.OrderBy(i => i, StringComparer.Ordinal).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
        return new(200, new JsonObject
        {
            ["characters"] = Ids(CharacterRegistry.AllIds),
            ["encounters"] = Ids(EncounterRegistry.AllIds),
            ["cards"] = Ids(CardRegistry.AllIds),
            ["relics"] = Ids(RelicRegistry.AllIds),
            ["potions"] = Ids(PotionRegistry.Models.AllIds),
            ["events"] = Ids(EventRegistry.AllIds)
        });
    }

    public ServiceResponse CreateRun(JsonObject body)
    {
        var config = NormalizeRunConfig(body);
        string id = "r" + Interlocked.Increment(ref _runCounter).ToString(CultureInfo.InvariantCulture);
        return Create(id, GameKind.Run, config);
    }

    public ServiceResponse CreateCombat(JsonObject body)
    {
        var config = NormalizeCombatConfig(body);
        string id = "c" + Interlocked.Increment(ref _combatCounter).ToString(CultureInfo.InvariantCulture);
        return Create(id, GameKind.Combat, config);
    }

    private ServiceResponse Create(string id, GameKind kind, JsonObject config)
    {
        if (_games.Count >= Options.MaxGames)
        {
            return Error(429, $"too many live games ({_games.Count}); DELETE some first");
        }
        GameSession g;
        try
        {
            g = Instantiate(id, kind, config);
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref CrashCount);
            WriteCrash(id, kind, config, Array.Empty<string>(), null, "create", 0, ex);
            return Crash(ex);
        }
        _games[id] = g;
        lock (g.Gate)
        {
            try
            {
                var obs = Observe(g);
                return new(200, new JsonObject { ["id"] = id, ["observation"] = obs });
            }
            catch (Exception ex)
            {
                return MarkCrashed(g, null, ex);
            }
        }
    }

    public static GameSession Instantiate(string id, GameKind kind, JsonObject config)
    {
        if (kind == GameKind.Run)
        {
            string seed = config["seed"]!.GetValue<string>();
            string character = config["character"]!.GetValue<string>();
            int ascension = config["ascension"]!.GetValue<int>();
            var content = config["content"]!.GetValue<string>() == "all" ? ContentFilter.All : ContentFilter.PortedOnly;
            var sim = RunSim.NewRun(new RunConfig(seed, character, ascension) { Content = content });
            return GameSession.ForRun(id, config["seed"]!.DeepClone(), config, sim);
        }
        ulong combatSeed = config["seed"]!.GetValue<ulong>();
        var deck = config["deck"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        string encounter = config["encounter"]!.GetValue<string>();
        int asc = config["ascension"]!.GetValue<int>();
        int? hp = config["hp"]?.GetValue<int>();
        int? maxHp = config["max_hp"]?.GetValue<int>();
        var combat = CombatSim.Create(combatSeed, deck, encounter, asc, hp, maxHp);
        return GameSession.ForCombat(id, config["seed"]!.DeepClone(), config, combat);
    }

    public static JsonObject NormalizeRunConfig(JsonObject body)
    {
        string seed = body["seed"] switch
        {
            null => RandomSeedText(),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Trim(),
            JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.ToJsonString(),
            _ => throw new BadRequestException("seed must be a string, a number or null")
        };
        if (seed.Length == 0)
        {
            seed = RandomSeedText();
        }
        string character = (OptString(body, "character") ?? StartingDecks.IroncladCharacterId).Trim().ToUpperInvariant();
        if (!CharacterRegistry.Defs.Contains(character))
        {
            throw new BadRequestException($"unknown character '{character}'; known: {string.Join(", ", CharacterRegistry.AllIds)}");
        }
        int ascension = OptInt(body, "ascension") ?? 0;
        if (ascension < 0 || ascension > 10)
        {
            throw new BadRequestException("ascension must be 0..10");
        }
        string content = (OptString(body, "content") ?? "ported_only").Trim().ToLowerInvariant();
        content = content switch
        {
            "all" => "all",
            "ported_only" or "ported" or "portedonly" => "ported_only",
            _ => throw new BadRequestException("content must be \"ported_only\" or \"all\"")
        };
        return new JsonObject { ["seed"] = seed, ["character"] = character, ["ascension"] = ascension, ["content"] = content };
    }

    public static JsonObject NormalizeCombatConfig(JsonObject body)
    {
        ulong seed = body["seed"] switch
        {
            null => (ulong)System.Random.Shared.NextInt64(1, long.MaxValue),
            JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.TryGetValue<ulong>(out var u) ? u : (ulong)v.GetValue<long>(),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => new RunRngSet(v.GetValue<string>()).Seed,
            _ => throw new BadRequestException("seed must be a number, a string or null")
        };
        string[] deck;
        if (body["deck"] is JsonArray arr)
        {
            deck = arr.Select(n => n?.GetValue<string>().Trim().ToUpperInvariant() ?? throw new BadRequestException("deck entries must be strings")).ToArray();
        }
        else if (body["deck"] == null)
        {
            deck = StartingDecks.Ironclad.ToArray();
        }
        else
        {
            throw new BadRequestException("deck must be an array of card ids");
        }
        foreach (var card in deck)
        {
            if (!CardRegistry.Defs.Contains(card))
            {
                throw new BadRequestException($"unknown card '{card}' (GET /catalog lists the cards)");
            }
        }
        string encounter = (OptString(body, "encounter") ?? throw new BadRequestException("encounter is required (GET /catalog lists the encounters)")).Trim().ToUpperInvariant();
        if (!EncounterRegistry.Defs.Contains(encounter))
        {
            throw new BadRequestException($"unknown encounter '{encounter}' (GET /catalog lists the encounters)");
        }
        int ascension = OptInt(body, "ascension") ?? 0;
        int? maxHp = OptInt(body, "max_hp");
        int? hp = OptInt(body, "hp");
        if (maxHp is <= 0 || hp is <= 0 || (hp != null && maxHp != null && hp > maxHp))
        {
            throw new BadRequestException("hp and max_hp must be positive and hp <= max_hp");
        }
        return new JsonObject
        {
            ["seed"] = seed,
            ["deck"] = new JsonArray(deck.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()),
            ["encounter"] = encounter,
            ["ascension"] = ascension,
            ["hp"] = hp,
            ["max_hp"] = maxHp
        };
    }

    public ServiceResponse GetGame(string id)
    {
        if (!_games.TryGetValue(id, out var g))
        {
            return Error(404, $"no game '{id}'");
        }
        lock (g.Gate)
        {
            if (g.Crashed)
            {
                return CrashedGame(g);
            }
            try
            {
                return new(200, new JsonObject { ["observation"] = Observe(g) });
            }
            catch (Exception ex)
            {
                return MarkCrashed(g, null, ex);
            }
        }
    }

    // GET /games/{id}/piles: full card nodes of every pile (draw pile sorted, so its order stays hidden).
    public ServiceResponse Piles(string id)
    {
        if (!_games.TryGetValue(id, out var g))
        {
            return Error(404, $"no game '{id}'");
        }
        lock (g.Gate)
        {
            if (g.Crashed)
            {
                return CrashedGame(g);
            }
            try
            {
                return new(200, ObservationBuilder.PilesNode(g));
            }
            catch (Exception ex)
            {
                return Error(500, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public ServiceResponse Step(string id, string token)
    {
        if (!_games.TryGetValue(id, out var g))
        {
            return Error(404, $"no game '{id}'");
        }
        lock (g.Gate)
        {
            if (g.Crashed)
            {
                return CrashedGame(g);
            }
            List<LegalEntry> legal;
            try
            {
                legal = ActionTokens.Enumerate(g);
            }
            catch (Exception ex)
            {
                return MarkCrashed(g, null, ex);
            }
            if (!ActionTokens.TryResolve(legal, token, out var entry, out var error))
            {
                return new(400, new JsonObject { ["ok"] = false, ["error"] = error, ["legal_actions"] = LegalNode(legal) });
            }
            var before = StepLog.Capture(g);
            try
            {
                Options.BeforeStep?.Invoke(g, entry.Action);
                g.Step(entry.Action);
            }
            catch (Exception ex)
            {
                return MarkCrashed(g, entry.Token, ex);
            }
            g.Tokens.Add(entry.Token);
            try
            {
                var obs = Observe(g);
                var log = new JsonArray { $"{entry.Token}: {entry.Desc}" };
                List<string> lines;
                try
                {
                    lines = StepLog.Diff(before, StepLog.Capture(g, before.Combat), entry.Action);
                }
                catch (Exception ex)
                {
                    // The log is a view: a bug in it must not mark the game crashed.
                    lines = new List<string> { $"(step log unavailable: {ex.GetType().Name}: {ex.Message})" };
                }
                foreach (var line in lines)
                {
                    log.Add(line);
                }
                return new(200, new JsonObject { ["observation"] = obs, ["log"] = log, ["ok"] = true });
            }
            catch (Exception ex)
            {
                return MarkCrashed(g, null, ex);
            }
        }
    }

    public ServiceResponse History(string id)
    {
        if (!_games.TryGetValue(id, out var g))
        {
            return Error(404, $"no game '{id}'");
        }
        lock (g.Gate)
        {
            var node = new JsonObject
            {
                ["id"] = g.Id,
                ["kind"] = g.Kind == GameKind.Run ? "run" : "combat",
                ["seed"] = g.Seed?.DeepClone(),
                ["config"] = g.Config.DeepClone(),
                ["tokens"] = new JsonArray(g.Tokens.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
                ["steps"] = g.Steps,
                ["crashed"] = g.Crashed
            };
            if (g.Crashed)
            {
                node["crash_token"] = g.CrashToken;
                node["error"] = g.CrashError;
            }
            return new(200, node);
        }
    }

    public ServiceResponse Delete(string id)
        => _games.TryRemove(id, out _) ? new(200, new JsonObject { ["ok"] = true }) : Error(404, $"no game '{id}'");

    public GameSession? Find(string id) => _games.TryGetValue(id, out var g) ? g : null;

    private static JsonObject Observe(GameSession g) => ObservationBuilder.Build(g, ActionTokens.Enumerate(g));

    private static JsonArray LegalNode(IEnumerable<LegalEntry> legal)
        => new(legal.Select(e => (JsonNode?)new JsonObject { ["token"] = e.Token, ["desc"] = e.Desc }).ToArray());

    private ServiceResponse MarkCrashed(GameSession g, string? token, Exception ex)
    {
        g.Crashed = true;
        g.CrashToken = token;
        g.CrashError = $"{ex.GetType().Name}: {ex.Message}";
        Interlocked.Increment(ref CrashCount);
        var tokens = new List<string>(g.Tokens);
        if (token != null)
        {
            tokens.Add(token);
        }
        string phase;
        try
        {
            phase = g.PhaseName;
        }
        catch (Exception)
        {
            phase = "?";
        }
        WriteCrash(g.Id, g.Kind, g.Config, tokens, token, phase, g.Steps, ex);
        return Crash(ex);
    }

    private static ServiceResponse Crash(Exception ex)
        => new(500, new JsonObject { ["ok"] = false, ["error"] = $"{ex.GetType().Name}: {ex.Message}", ["crashed"] = true });

    private static ServiceResponse CrashedGame(GameSession g)
        => new(409, new JsonObject { ["ok"] = false, ["error"] = $"game {g.Id} crashed earlier ({g.CrashError}); GET /games/{g.Id}/history has the repro", ["crashed"] = true });

    private void WriteCrash(string id, GameKind kind, JsonObject config, IReadOnlyList<string> tokens, string? crashToken, string phase, int steps, Exception ex)
    {
        string? path = Options.CrashLogPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var node = new JsonObject
        {
            ["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["version"] = Options.Version,
            ["game_id"] = id,
            ["kind"] = kind == GameKind.Run ? "run" : "combat",
            ["seed"] = config["seed"]?.DeepClone(),
            ["config"] = config.DeepClone(),
            ["tokens"] = new JsonArray(tokens.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["crash_token"] = crashToken,
            ["steps"] = steps,
            ["phase"] = phase,
            ["error"] = $"{ex.GetType().Name}: {ex.Message}",
            ["content_gap"] = ex is NotImplementedException,
            ["stack"] = ex.StackTrace
        };
        string line = node.ToJsonString(JsonOptions);
        lock (CrashLogGate)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(path, line + "\n");
            }
            catch (Exception io)
            {
                Console.Error.WriteLine($"crash log write failed ({path}): {io.Message}");
            }
        }
    }

    public static ServiceResponse Error(int status, string message)
        => new(status, new JsonObject { ["ok"] = false, ["error"] = message });

    private static JsonObject ParseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException("invalid JSON body: " + ex.Message);
        }
        return node as JsonObject ?? throw new BadRequestException("the JSON body must be an object");
    }

    private static string? OptString(JsonObject body, string key)
        => body[key] switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => throw new BadRequestException($"{key} must be a string")
        };

    private static int? OptInt(JsonObject body, string key)
        => body[key] switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i) => i,
            _ => throw new BadRequestException($"{key} must be an integer or null")
        };

    private static string RandomSeedText()
    {
        Span<char> chars = stackalloc char[10];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = SeedAlphabet[System.Random.Shared.Next(SeedAlphabet.Length)];
        }
        return new string(chars);
    }
}

public sealed class BadRequestException : Exception
{
    public BadRequestException(string message)
        : base(message)
    {
    }
}
