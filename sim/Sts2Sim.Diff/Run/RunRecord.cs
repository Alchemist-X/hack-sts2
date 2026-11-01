using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Run;

public sealed record RunAncientChoice(string TextKey, bool WasChosen);

public sealed record RunFloorRoom(RoomType RoomType, string? ModelId, IReadOnlyList<string> MonsterIds);

public sealed class RunFloor
{
    public MapPointType PointType { get; init; }

    public List<RunFloorRoom> Rooms { get; init; } = new();

    public List<RunAncientChoice> AncientChoices { get; init; } = new();
}

public enum RunRecordKind
{
    History,
    Save
}

public sealed class RunRecord
{
    public const int SupportedHistorySchemaVersion = 10;

    public const string SupportedBuildId = "v0.111.0";

    public string Source { get; init; } = string.Empty;

    public RunRecordKind Kind { get; init; }

    public int SchemaVersion { get; init; }

    public string? BuildId { get; init; }

    public string GameMode { get; init; } = SavedRun.StandardGameMode;

    public string Seed { get; init; } = string.Empty;

    public int Ascension { get; init; }

    public string CharacterId { get; init; } = string.Empty;

    public int PlayerCount { get; init; } = 1;

    public List<string> ActIds { get; init; } = new();

    public List<string> ModifierIds { get; init; } = new();

    public List<List<RunFloor>> Floors { get; init; } = new();

    public List<string> FinalRelicIds { get; init; } = new();

    public List<string> FinalDeckIds { get; init; } = new();

    public SavedRun? Save { get; init; }

    public IEnumerable<string> UnsupportedReasons()
    {
        if (Kind == RunRecordKind.History)
        {
            if (SchemaVersion != SupportedHistorySchemaVersion)
            {
                yield return $".run schema_version {SchemaVersion} (only {SupportedHistorySchemaVersion})";
            }
            if (!string.Equals(BuildId, SupportedBuildId, StringComparison.Ordinal))
            {
                yield return $"build {BuildId ?? "<none>"} (rules are {SupportedBuildId})";
            }
        }
        else if (SchemaVersion != SavedRun.SupportedSchemaVersion)
        {
            yield return $"save schema_version {SchemaVersion} (only {SavedRun.SupportedSchemaVersion})";
        }
        if (!string.Equals(GameMode, SavedRun.StandardGameMode, StringComparison.Ordinal))
        {
            yield return $"game_mode {GameMode}";
        }
        if (ModifierIds.Count > 0)
        {
            yield return "modifiers " + string.Join(",", ModifierIds);
        }
        if (PlayerCount != 1)
        {
            yield return $"{PlayerCount} players";
        }
        if (string.IsNullOrEmpty(Seed))
        {
            yield return "no seed";
        }
    }
}

public static class RunRecordReader
{
    public static RunRecord ReadHistoryFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadHistory(File.ReadAllText(path), path);
    }

    public static RunRecord ReadHistory(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var players = Array(root, "players");
        var first = players.Count > 0 ? players[0] : default;
        return new RunRecord
        {
            Source = source,
            Kind = RunRecordKind.History,
            SchemaVersion = Int(root, "schema_version"),
            BuildId = Str(root, "build_id"),
            GameMode = Str(root, "game_mode") ?? SavedRun.StandardGameMode,
            Seed = Str(root, "seed") ?? string.Empty,
            Ascension = Int(root, "ascension"),
            CharacterId = first.ValueKind == JsonValueKind.Object ? RunSaveReader.StripCategory(Str(first, "character") ?? string.Empty) : string.Empty,
            PlayerCount = players.Count,
            ActIds = Array(root, "acts").Where(a => a.ValueKind == JsonValueKind.String).Select(a => RunSaveReader.StripCategory(a.GetString()!)).ToList(),
            ModifierIds = ModifierIds(root),
            Floors = ReadFloors(root),
            FinalRelicIds = first.ValueKind == JsonValueKind.Object ? Ids(first, "relics") : new List<string>(),
            FinalDeckIds = first.ValueKind == JsonValueKind.Object ? Ids(first, "deck") : new List<string>()
        };
    }

    public static RunRecord FromSave(SavedRun save, string source)
    {
        ArgumentNullException.ThrowIfNull(save);
        List<List<RunFloor>> floors;
        if (save.Json.Length > 0)
        {
            using var doc = JsonDocument.Parse(save.Json);
            floors = ReadFloors(doc.RootElement);
        }
        else
        {
            floors = new List<List<RunFloor>>();
        }
        var player = save.Players.Count > 0 ? save.Players[0] : null;
        return new RunRecord
        {
            Source = source,
            Kind = RunRecordKind.Save,
            SchemaVersion = save.SchemaVersion,
            GameMode = save.GameMode,
            Seed = save.Rng.Seed,
            Ascension = save.Ascension,
            CharacterId = player?.CharacterId ?? string.Empty,
            PlayerCount = save.Players.Count,
            ActIds = save.Acts.Select(a => a.Id).ToList(),
            ModifierIds = save.Modifiers.Select(m => m.Id).ToList(),
            Floors = floors,
            FinalRelicIds = player?.Relics.Select(r => r.Id).ToList() ?? new List<string>(),
            FinalDeckIds = player?.Deck.Select(c => c.Id).ToList() ?? new List<string>(),
            Save = save
        };
    }

    public static List<List<RunFloor>> ReadFloors(JsonElement root)
    {
        var acts = new List<List<RunFloor>>();
        foreach (var act in Array(root, "map_point_history"))
        {
            var floors = new List<RunFloor>();
            if (act.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in act.EnumerateArray())
                {
                    floors.Add(ReadFloor(entry));
                }
            }
            acts.Add(floors);
        }
        return acts;
    }

    private static RunFloor ReadFloor(JsonElement entry)
    {
        var floor = new RunFloor { PointType = ParseEnum<MapPointType>(Str(entry, "map_point_type")) };
        foreach (var room in Array(entry, "rooms"))
        {
            var monsters = Array(room, "monster_ids").Where(m => m.ValueKind == JsonValueKind.String).Select(m => RunSaveReader.StripCategory(m.GetString()!)).ToList();
            string? model = Str(room, "model_id");
            floor.Rooms.Add(new RunFloorRoom(ParseEnum<RoomType>(Str(room, "room_type")), model != null ? RunSaveReader.StripCategory(model) : null, monsters));
        }
        var stats = Array(entry, "player_stats");
        if (stats.Count > 0)
        {
            foreach (var choice in Array(stats[0], "ancient_choice"))
            {
                string? key = Str(choice, "TextKey");
                if (key != null)
                {
                    floor.AncientChoices.Add(new RunAncientChoice(key, choice.TryGetProperty("was_chosen", out var c) && c.ValueKind == JsonValueKind.True));
                }
            }
        }
        return floor;
    }

    public static TEnum ParseEnum<TEnum>(string? snake) where TEnum : struct, Enum
    {
        if (snake == null)
        {
            return default;
        }
        string pascal = snake.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<TEnum>(pascal, ignoreCase: true, out var value) ? value : throw new FormatException($"Unknown {typeof(TEnum).Name} '{snake}'.");
    }

    private static List<string> ModifierIds(JsonElement root)
    {
        var ids = new List<string>();
        foreach (var m in Array(root, "modifiers"))
        {
            if (m.ValueKind == JsonValueKind.String)
            {
                ids.Add(RunSaveReader.StripCategory(m.GetString()!));
            }
            else if (m.ValueKind == JsonValueKind.Object && Str(m, "id") is { } id)
            {
                ids.Add(RunSaveReader.StripCategory(id));
            }
        }
        return ids;
    }

    private static List<string> Ids(JsonElement e, string name)
        => Array(e, name).Select(x => x.ValueKind == JsonValueKind.Object ? Str(x, "id") : null).Where(x => x != null).Select(x => RunSaveReader.StripCategory(x!)).ToList();

    private static List<JsonElement> Array(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement>();

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
