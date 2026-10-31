using System.Globalization;

namespace Sts2Sim.Diff;

public readonly record struct CardCost(bool IsX, int Value)
{
    public const string XLiteral = "X";

    public static CardCost X => new(true, 0);

    public static CardCost Of(int value) => new(false, value);

    public JsonNode ToJson() => IsX ? JsonValue.Create(XLiteral) : JsonValue.Create(Value);

    public override string ToString() => IsX ? XLiteral : Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record TurnProjection(int PlayerTurn, string Phase);

public readonly record struct HandCardProjection(string Id, bool Upgraded, CardCost Cost, bool CanPlay);

public readonly record struct PowerProjection(string Id, int Amount);

public sealed class IntentProjection
{
    public required string Type { get; init; }

    public int? AttackValue { get; init; }

    public int? AttackTimes { get; init; }

    public IReadOnlyDictionary<string, JsonNode?> Extras { get; init; } = EmptyExtras;

    internal static readonly IReadOnlyDictionary<string, JsonNode?> EmptyExtras = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
}

public sealed class PlayerProjection
{
    public int Hp { get; init; }

    public int MaxHp { get; init; }

    public int Block { get; init; }

    public int Energy { get; init; }

    public int MaxEnergy { get; init; }

    public IReadOnlyList<HandCardProjection> Hand { get; init; } = Array.Empty<HandCardProjection>();

    public int DrawCount { get; init; }

    public int DiscardCount { get; init; }

    public int ExhaustCount { get; init; }

    public IReadOnlyList<string>? DrawMultiset { get; init; }

    public IReadOnlyList<string>? Discard { get; init; }

    public IReadOnlyList<string>? Exhaust { get; init; }

    public IReadOnlyList<PowerProjection> Powers { get; init; } = Array.Empty<PowerProjection>();
}

public sealed class EnemyProjection
{
    public int CombatId { get; init; }

    public required string MonsterId { get; init; }

    public int Hp { get; init; }

    public int MaxHp { get; init; }

    public int Block { get; init; }

    public IReadOnlyList<PowerProjection> Powers { get; init; } = Array.Empty<PowerProjection>();

    public IReadOnlyList<IntentProjection> Intents { get; init; } = Array.Empty<IntentProjection>();
}

public sealed class Projection
{
    public required TurnProjection Turn { get; init; }

    public required PlayerProjection Player { get; init; }

    public IReadOnlyList<EnemyProjection> Enemies { get; init; } = Array.Empty<EnemyProjection>();
}

public static class ProjectionJson
{
    public const string PhasePlayerPlay = "player_play";

    public static string CardKey(string id, bool upgraded) => upgraded ? id + "+" : id;

    public static JsonObject Write(Projection p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var player = p.Player;
        var hand = new JsonArray();
        foreach (var c in player.Hand)
        {
            hand.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["upgraded"] = c.Upgraded,
                ["cost"] = c.Cost.ToJson(),
                ["can_play"] = c.CanPlay
            });
        }
        var playerObj = new JsonObject
        {
            ["hp"] = player.Hp,
            ["max_hp"] = player.MaxHp,
            ["block"] = player.Block,
            ["energy"] = player.Energy,
            ["max_energy"] = player.MaxEnergy,
            ["hand"] = hand,
            ["draw_count"] = player.DrawCount,
            ["discard_count"] = player.DiscardCount,
            ["exhaust_count"] = player.ExhaustCount
        };
        if (player.DrawMultiset != null)
        {
            playerObj["draw_multiset"] = StringArray(player.DrawMultiset);
        }
        if (player.Discard != null)
        {
            playerObj["discard"] = StringArray(player.Discard);
        }
        if (player.Exhaust != null)
        {
            playerObj["exhaust"] = StringArray(player.Exhaust);
        }
        playerObj["powers"] = PowersArray(player.Powers);
        var enemies = new JsonArray();
        foreach (var e in p.Enemies)
        {
            var intents = new JsonArray();
            foreach (var intent in e.Intents)
            {
                intents.Add(WriteIntent(intent));
            }
            enemies.Add(new JsonObject
            {
                ["combat_id"] = e.CombatId,
                ["monster_id"] = e.MonsterId,
                ["hp"] = e.Hp,
                ["max_hp"] = e.MaxHp,
                ["block"] = e.Block,
                ["powers"] = PowersArray(e.Powers),
                ["intents"] = intents
            });
        }
        return new JsonObject
        {
            ["turn"] = new JsonObject
            {
                ["player_turn"] = p.Turn.PlayerTurn,
                ["phase"] = p.Turn.Phase
            },
            ["player"] = playerObj,
            ["enemies"] = enemies
        };
    }

    public static JsonObject WriteIntent(IntentProjection intent)
    {
        var obj = new JsonObject { ["type"] = intent.Type };
        if (intent.AttackValue is int value)
        {
            obj["attack_value"] = value;
        }
        if (intent.AttackTimes is int times)
        {
            obj["attack_times"] = times;
        }
        foreach (var (key, extra) in intent.Extras)
        {
            obj[key] = extra?.DeepClone();
        }
        return obj;
    }

    public static Projection Read(JsonElement e, string path = "pre_state", ISet<string>? ignoredKeys = null)
    {
        var r = new JsonReadHelper(ignoredKeys);
        r.RequireObject(e, path);
        r.NoteUnknownKeys(e, path, "turn", "player", "enemies");
        var turnEl = r.Property(e, "turn", path);
        r.RequireObject(turnEl, path + ".turn");
        r.NoteUnknownKeys(turnEl, path + ".turn", "player_turn", "phase");
        var turn = new TurnProjection(r.Int(turnEl, "player_turn", path + ".turn"), r.String(turnEl, "phase", path + ".turn"));

        var pe = r.Property(e, "player", path);
        string pp = path + ".player";
        r.RequireObject(pe, pp);
        r.NoteUnknownKeys(pe, pp, "hp", "max_hp", "block", "energy", "max_energy", "hand", "draw_count", "discard_count",
            "exhaust_count", "draw_multiset", "discard", "exhaust", "powers");
        var hand = new List<HandCardProjection>();
        var handEl = r.Property(pe, "hand", pp);
        r.RequireArray(handEl, pp + ".hand");
        int hi = 0;
        foreach (var c in handEl.EnumerateArray())
        {
            string cp = $"{pp}.hand[{hi}]";
            r.RequireObject(c, cp);
            r.NoteUnknownKeys(c, cp, "id", "upgraded", "cost", "can_play");
            hand.Add(new HandCardProjection(r.String(c, "id", cp), r.Bool(c, "upgraded", cp), r.Cost(c, "cost", cp), r.Bool(c, "can_play", cp)));
            hi++;
        }
        var player = new PlayerProjection
        {
            Hp = r.Int(pe, "hp", pp),
            MaxHp = r.Int(pe, "max_hp", pp),
            Block = r.Int(pe, "block", pp),
            Energy = r.Int(pe, "energy", pp),
            MaxEnergy = r.Int(pe, "max_energy", pp),
            Hand = hand,
            DrawCount = r.Int(pe, "draw_count", pp),
            DiscardCount = r.Int(pe, "discard_count", pp),
            ExhaustCount = r.Int(pe, "exhaust_count", pp),
            DrawMultiset = r.OptionalStringList(pe, "draw_multiset", pp),
            Discard = r.OptionalStringList(pe, "discard", pp),
            Exhaust = r.OptionalStringList(pe, "exhaust", pp),
            Powers = ReadPowers(r, r.Property(pe, "powers", pp), pp + ".powers")
        };

        var enemies = new List<EnemyProjection>();
        var enemiesEl = r.Property(e, "enemies", path);
        r.RequireArray(enemiesEl, path + ".enemies");
        int ei = 0;
        foreach (var en in enemiesEl.EnumerateArray())
        {
            string ep = $"{path}.enemies[{ei}]";
            r.RequireObject(en, ep);
            r.NoteUnknownKeys(en, ep, "combat_id", "monster_id", "hp", "max_hp", "block", "powers", "intents");
            var intents = new List<IntentProjection>();
            var intentsEl = r.Property(en, "intents", ep);
            r.RequireArray(intentsEl, ep + ".intents");
            int ii = 0;
            foreach (var it in intentsEl.EnumerateArray())
            {
                intents.Add(ReadIntent(r, it, $"{ep}.intents[{ii}]"));
                ii++;
            }
            enemies.Add(new EnemyProjection
            {
                CombatId = r.Int(en, "combat_id", ep),
                MonsterId = r.String(en, "monster_id", ep),
                Hp = r.Int(en, "hp", ep),
                MaxHp = r.Int(en, "max_hp", ep),
                Block = r.Int(en, "block", ep),
                Powers = ReadPowers(r, r.Property(en, "powers", ep), ep + ".powers"),
                Intents = intents
            });
            ei++;
        }
        return new Projection { Turn = turn, Player = player, Enemies = enemies };
    }

    public static Projection Read(JsonNode node, string path = "pre_state", ISet<string>? ignoredKeys = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return Read(doc.RootElement.Clone(), path, ignoredKeys);
    }

    private static IntentProjection ReadIntent(JsonReadHelper r, JsonElement it, string ip)
    {
        r.RequireObject(it, ip);
        string type = r.String(it, "type", ip);
        int? attackValue = r.OptionalInt(it, "attack_value", ip);
        int? attackTimes = r.OptionalInt(it, "attack_times", ip);
        if (attackValue.HasValue && !attackTimes.HasValue)
        {
            attackTimes = 1;
        }
        SortedDictionary<string, JsonNode?>? extras = null;
        foreach (var prop in it.EnumerateObject())
        {
            if (prop.Name is "type" or "attack_value" or "attack_times")
            {
                continue;
            }
            extras ??= new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
            extras[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }
        return new IntentProjection
        {
            Type = type,
            AttackValue = attackValue,
            AttackTimes = attackTimes,
            Extras = extras ?? IntentProjection.EmptyExtras
        };
    }

    private static List<PowerProjection> ReadPowers(JsonReadHelper r, JsonElement el, string path)
    {
        r.RequireArray(el, path);
        var list = new List<PowerProjection>();
        int i = 0;
        foreach (var pw in el.EnumerateArray())
        {
            string p = $"{path}[{i}]";
            r.RequireObject(pw, p);
            r.NoteUnknownKeys(pw, p, "id", "amount");
            list.Add(new PowerProjection(r.String(pw, "id", p), r.Int(pw, "amount", p)));
            i++;
        }
        return list;
    }

    private static JsonArray StringArray(IReadOnlyList<string> items)
    {
        var a = new JsonArray();
        foreach (var s in items)
        {
            a.Add(s);
        }
        return a;
    }

    private static JsonArray PowersArray(IReadOnlyList<PowerProjection> powers)
    {
        var a = new JsonArray();
        foreach (var pw in powers)
        {
            a.Add(new JsonObject { ["id"] = pw.Id, ["amount"] = pw.Amount });
        }
        return a;
    }
}
