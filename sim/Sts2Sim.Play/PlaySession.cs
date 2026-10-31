using System.Buffers;
using System.Text.Encodings.Web;

namespace Sts2Sim.Play;

public class PlayException : Exception
{
    public PlayException(string message, int exitCode) : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

public sealed class UsageException : PlayException
{
    public UsageException(string message) : base(message, PlayCli.ExitUsage)
    {
    }
}

public sealed class SessionFormatException : PlayException
{
    public SessionFormatException(string message) : base(message, PlayCli.ExitUsage)
    {
    }
}

public sealed class SessionReplayException : PlayException
{
    public SessionReplayException(string message) : base(message, PlayCli.ExitInternal)
    {
    }
}

public readonly record struct SessionAction(int K, GameAction Action, string Label);

public sealed class PlaySession
{
    public const string FormatId = "sts2sim-play-session/1";

    public const int DefaultActionCap = 100;

    public const string AgentPolicy = "agent";

    internal static readonly JavaScriptEncoder JsonEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public required ulong Seed { get; init; }

    public required string EncounterId { get; init; }

    public required int Ascension { get; init; }

    public int? CurrentHp { get; init; }

    public int? MaxHp { get; init; }

    public int ActionCap { get; init; } = DefaultActionCap;

    public string Policy { get; init; } = AgentPolicy;

    public List<SessionAction> Actions { get; } = new();

    public int InvalidAttempts { get; set; }

    public string? StateHash { get; set; }

    public static PlaySession Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SessionFormatException($"cannot read session file {path}: {ex.Message}");
        }
        return FromJson(text);
    }

    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        string tmp = full + ".tmp" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        File.WriteAllText(tmp, ToJson(), Utf8NoBom);
        File.Move(tmp, full, overwrite: true);
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JsonEncoder }))
        {
            w.WriteStartObject();
            w.WriteString("format", FormatId);
            w.WriteNumber("seed", Seed);
            w.WriteString("encounter_id", EncounterId);
            w.WriteNumber("ascension", Ascension);
            WriteNullableInt(w, "current_hp", CurrentHp);
            WriteNullableInt(w, "max_hp", MaxHp);
            w.WriteNumber("action_cap", ActionCap);
            w.WriteString("policy", Policy);
            w.WriteStartArray("actions");
            for (int i = 0; i < Actions.Count; i++)
            {
                var a = Actions[i];
                w.WriteStartObject();
                w.WriteNumber("k", a.K);
                if (a.Action.Kind == ActionKind.EndTurn)
                {
                    w.WriteString("kind", "end");
                }
                else
                {
                    w.WriteString("kind", "play");
                    w.WriteNumber("card", a.Action.CardIndex);
                    w.WriteNumber("target", a.Action.TargetIndex);
                }
                w.WriteString("label", a.Label);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("invalid_attempts", InvalidAttempts);
            if (StateHash is null)
            {
                w.WriteNull("state_hash");
            }
            else
            {
                w.WriteString("state_hash", StateHash);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static PlaySession FromJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SessionFormatException("the session file is not valid JSON: " + ex.Message);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SessionFormatException("the session file must hold a JSON object");
            }
            string format = ReadString(root, "format");
            if (!string.Equals(format, FormatId, StringComparison.Ordinal))
            {
                throw new SessionFormatException($"format is '{format}', expected '{FormatId}'");
            }
            var session = new PlaySession
            {
                Seed = ReadUInt64(root, "seed"),
                EncounterId = ReadString(root, "encounter_id"),
                Ascension = ReadInt(root, "ascension"),
                CurrentHp = ReadNullableInt(root, "current_hp"),
                MaxHp = ReadNullableInt(root, "max_hp"),
                ActionCap = ReadInt(root, "action_cap"),
                Policy = ReadString(root, "policy"),
                InvalidAttempts = ReadInt(root, "invalid_attempts"),
                StateHash = ReadNullableString(root, "state_hash")
            };
            if (session.ActionCap < 1)
            {
                throw new SessionFormatException($"action_cap must be positive (got {session.ActionCap})");
            }
            if (session.InvalidAttempts < 0)
            {
                throw new SessionFormatException($"invalid_attempts must not be negative (got {session.InvalidAttempts})");
            }
            var actions = Property(root, "actions");
            if (actions.ValueKind != JsonValueKind.Array)
            {
                throw new SessionFormatException("actions must be an array");
            }
            int index = 0;
            foreach (var item in actions.EnumerateArray())
            {
                session.Actions.Add(ReadAction(item, index++));
            }
            return session;
        }
    }

    private static SessionAction ReadAction(JsonElement item, int index)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new SessionFormatException($"actions[{index}] is not an object");
        }
        int k = ReadInt(item, "k");
        string kind = ReadString(item, "kind");
        string label = item.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : string.Empty;
        var action = kind switch
        {
            "end" => GameAction.EndTurn,
            "play" => GameAction.Play(ReadInt(item, "card"), ReadInt(item, "target")),
            _ => throw new SessionFormatException($"actions[{index}].kind is '{kind}', expected 'play' or 'end'")
        };
        return new SessionAction(k, action, label);
    }

    private static void WriteNullableInt(Utf8JsonWriter w, string name, int? value)
    {
        if (value is int v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static JsonElement Property(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            throw new SessionFormatException($"missing field '{name}'");
        }
        return value;
    }

    private static string ReadString(JsonElement obj, string name)
    {
        var value = Property(obj, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SessionFormatException($"field '{name}' must be a string");
        }
        return value.GetString()!;
    }

    private static string? ReadNullableString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SessionFormatException($"field '{name}' must be a string or null");
        }
        return value.GetString();
    }

    private static int ReadInt(JsonElement obj, string name)
    {
        var value = Property(obj, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new SessionFormatException($"field '{name}' must be a 32-bit integer");
        }
        return result;
    }

    private static int? ReadNullableInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new SessionFormatException($"field '{name}' must be a 32-bit integer or null");
        }
        return result;
    }

    private static ulong ReadUInt64(JsonElement obj, string name)
    {
        var value = Property(obj, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong result))
        {
            throw new SessionFormatException($"field '{name}' must be an unsigned 64-bit integer");
        }
        return result;
    }
}
