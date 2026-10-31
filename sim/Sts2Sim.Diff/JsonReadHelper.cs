using System.Text.RegularExpressions;

namespace Sts2Sim.Diff;

public sealed class BundleFormatException : Exception
{
    public BundleFormatException(string path, string message)
        : base($"{path}: {message}")
    {
        Path = path;
    }

    public string Path { get; }
}

internal sealed class JsonReadHelper
{
    private static readonly Regex IndexRegex = new(@"\[\d+\]", RegexOptions.CultureInvariant);

    private readonly ISet<string>? _ignoredKeys;

    public JsonReadHelper(ISet<string>? ignoredKeys)
    {
        _ignoredKeys = ignoredKeys;
    }

    public static string PathPattern(string path) => IndexRegex.Replace(path, "[*]");

    public void RequireObject(JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw new BundleFormatException(path, $"expected an object, got {e.ValueKind}");
        }
    }

    public void RequireArray(JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Array)
        {
            throw new BundleFormatException(path, $"expected an array, got {e.ValueKind}");
        }
    }

    public JsonElement Property(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            throw new BundleFormatException(path, $"missing required field '{name}'");
        }
        return value;
    }

    public bool TryProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }
        value = default;
        return false;
    }

    public int Int(JsonElement obj, string name, string path) => AsInt(Property(obj, name, path), $"{path}.{name}");

    public int? OptionalInt(JsonElement obj, string name, string path)
        => TryProperty(obj, name, out var v) ? AsInt(v, $"{path}.{name}") : null;

    public bool Bool(JsonElement obj, string name, string path)
    {
        var v = Property(obj, name, path);
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new BundleFormatException($"{path}.{name}", $"expected a boolean, got {v.ValueKind}")
        };
    }

    public string String(JsonElement obj, string name, string path)
    {
        var v = Property(obj, name, path);
        if (v.ValueKind != JsonValueKind.String)
        {
            throw new BundleFormatException($"{path}.{name}", $"expected a string, got {v.ValueKind}");
        }
        return v.GetString()!;
    }

    public string? OptionalString(JsonElement obj, string name, string path)
    {
        if (!TryProperty(obj, name, out var v))
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            throw new BundleFormatException($"{path}.{name}", $"expected a string, got {v.ValueKind}");
        }
        return v.GetString();
    }

    public CardCost Cost(JsonElement obj, string name, string path)
    {
        var v = Property(obj, name, path);
        if (v.ValueKind == JsonValueKind.String)
        {
            string s = v.GetString()!;
            if (s == CardCost.XLiteral)
            {
                return CardCost.X;
            }
            throw new BundleFormatException($"{path}.{name}", $"a string cost must be \"{CardCost.XLiteral}\", got \"{s}\"");
        }
        return CardCost.Of(AsInt(v, $"{path}.{name}"));
    }

    public List<string> StringList(JsonElement obj, string name, string path)
    {
        var v = Property(obj, name, path);
        return AsStringList(v, $"{path}.{name}");
    }

    public List<string>? OptionalStringList(JsonElement obj, string name, string path)
        => TryProperty(obj, name, out var v) ? AsStringList(v, $"{path}.{name}") : null;

    public void NoteUnknownKeys(JsonElement obj, string path, params string[] known)
    {
        if (_ignoredKeys == null)
        {
            return;
        }
        foreach (var prop in obj.EnumerateObject())
        {
            if (Array.IndexOf(known, prop.Name) < 0)
            {
                _ignoredKeys.Add($"{PathPattern(path)}.{prop.Name}");
            }
        }
    }

    public static int AsInt(JsonElement v, string path)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int value))
        {
            throw new BundleFormatException(path, $"expected a 32-bit integer, got {Describe(v)}");
        }
        return value;
    }

    private static List<string> AsStringList(JsonElement v, string path)
    {
        if (v.ValueKind != JsonValueKind.Array)
        {
            throw new BundleFormatException(path, $"expected an array of strings, got {v.ValueKind}");
        }
        var list = new List<string>(v.GetArrayLength());
        int i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new BundleFormatException($"{path}[{i}]", $"expected a string, got {item.ValueKind}");
            }
            list.Add(item.GetString()!);
            i++;
        }
        return list;
    }

    private static string Describe(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.ValueKind.ToString();
}
