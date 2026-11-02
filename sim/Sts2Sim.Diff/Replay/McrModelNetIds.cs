using System.Text;

namespace Sts2Sim.Diff.Replay;

public sealed class McrModelNetIds
{
    public const string NoneEntry = "NONE";

    private readonly List<string> _categories = new() { NoneEntry };
    private readonly List<string> _entries = new() { NoneEntry };
    private readonly List<string> _properties = new();
    private readonly List<string> _epochs = new();

    public McrModelNetIds(string gameVersion, bool legacy, IEnumerable<string> models, IEnumerable<string> properties, IEnumerable<string> epochs)
    {
        GameVersion = gameVersion;
        Legacy = legacy;
        var categorySet = new HashSet<string>(StringComparer.Ordinal) { NoneEntry };
        var entrySet = new HashSet<string>(StringComparer.Ordinal) { NoneEntry };
        var hash = new XxHash32();
        var ordered = models.ToList();
        if (legacy)
        {
            ordered.Sort((a, b) => string.CompareOrdinal(a[(a.IndexOf('|') + 1)..], b[(b.IndexOf('|') + 1)..]));
            ordered = ordered.Select(row => row[..row.IndexOf('|')]).ToList();
        }
        foreach (string id in ordered)
        {
            int dot = id.IndexOf('.');
            string category = id[..dot];
            string entry = id[(dot + 1)..];
            if (categorySet.Add(category))
            {
                _categories.Add(category);
            }
            if (entrySet.Add(entry))
            {
                _entries.Add(entry);
            }
            hash.Append(Encoding.UTF8.GetBytes(category));
            hash.Append(Encoding.UTF8.GetBytes(entry));
        }
        foreach (string p in properties)
        {
            _properties.Add(p);
            if (!legacy)
            {
                hash.Append(Encoding.UTF8.GetBytes(p));
            }
        }
        var epochSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (string e in epochs)
        {
            if (epochSet.Add(e))
            {
                _epochs.Add(e);
            }
            hash.Append(Encoding.UTF8.GetBytes(e));
        }
        CategoryIdBitSize = Bits(_categories.Count);
        EntryIdBitSize = Bits(_entries.Count);
        PropertyIdBitSize = Bits(_properties.Count);
        EpochIdBitSize = Bits(_epochs.Count);
        if (legacy)
        {
            hash.Append(BitConverter.GetBytes(_categories.Count - 1));
            hash.Append(BitConverter.GetBytes(_entries.Count - 1));
            hash.Append(BitConverter.GetBytes(_epochs.Count - 1));
        }
        Hash = hash.GetCurrentHash();
    }

    public static McrModelNetIds V0111 { get; } = new(ModelNetIdTableV0111.GameVersion, ModelNetIdTableV0111.LegacyHash,
        ModelNetIdTableV0111.Models, ModelNetIdTableV0111.Properties, ModelNetIdTableV0111.Epochs);

    public static McrModelNetIds V0107 { get; } = new(ModelNetIdTableV0107.GameVersion, ModelNetIdTableV0107.LegacyHash,
        ModelNetIdTableV0107.Models, ModelNetIdTableV0107.Properties, ModelNetIdTableV0107.Epochs);

    public static IReadOnlyList<McrModelNetIds> Known { get; } = new[] { V0111, V0107 };

    public static McrModelNetIds? ForHash(uint hash) => Known.FirstOrDefault(t => t.Hash == hash);

    public static McrModelNetIds? ForVersion(string version)
    {
        if (version.StartsWith("v0.107.", StringComparison.Ordinal))
        {
            return V0107;
        }
        if (version.StartsWith("v0.110.", StringComparison.Ordinal) || version.StartsWith("v0.111.", StringComparison.Ordinal))
        {
            return V0111;
        }
        return null;
    }

    public bool Legacy { get; }


    public string GameVersion { get; }

    public int CategoryIdBitSize { get; }

    public int EntryIdBitSize { get; }

    public int PropertyIdBitSize { get; }

    public int EpochIdBitSize { get; }

    public uint Hash { get; }

    public int CategoryCount => _categories.Count;

    public int EntryCount => _entries.Count;

    public int PropertyCount => _properties.Count;

    public int EpochCount => _epochs.Count;

    private static int Bits(int count) => (int)Math.Ceiling(Math.Log2(count));

    public string Category(int netId) => Lookup(_categories, netId, "category");

    public string Entry(int netId) => Lookup(_entries, netId, "entry");

    public string Property(int netId) => Lookup(_properties, netId, "property");

    public string Epoch(int netId) => Lookup(_epochs, netId, "epoch");

    private static string Lookup(List<string> list, int netId, string kind)
    {
        if (netId < 0 || netId >= list.Count)
        {
            throw new McrFormatException($"{kind} net id {netId} out of range ({list.Count})");
        }
        return list[netId];
    }
}

public sealed class XxHash32
{
    private const uint Prime1 = 2654435761u;
    private const uint Prime2 = 2246822519u;
    private const uint Prime3 = 3266489917u;
    private const uint Prime4 = 668265263u;
    private const uint Prime5 = 374761393u;

    private uint _v1 = unchecked(Prime1 + Prime2);
    private uint _v2 = Prime2;
    private uint _v3 = 0;
    private uint _v4 = unchecked(0 - Prime1);
    private readonly byte[] _pending = new byte[16];
    private int _pendingCount;
    private long _total;

    private static uint Rotl(uint x, int r) => (x << r) | (x >> (32 - r));

    private static uint Round(uint acc, uint lane) => unchecked(Rotl(acc + lane * Prime2, 13) * Prime1);

    private static uint Lane(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    public void Append(byte[] data)
    {
        _total += data.Length;
        foreach (byte d in data)
        {
            _pending[_pendingCount++] = d;
            if (_pendingCount == 16)
            {
                _v1 = Round(_v1, Lane(_pending, 0));
                _v2 = Round(_v2, Lane(_pending, 4));
                _v3 = Round(_v3, Lane(_pending, 8));
                _v4 = Round(_v4, Lane(_pending, 12));
                _pendingCount = 0;
            }
        }
    }

    public uint GetCurrentHash()
    {
        uint h;
        unchecked
        {
            h = _total >= 16
                ? Rotl(_v1, 1) + Rotl(_v2, 7) + Rotl(_v3, 12) + Rotl(_v4, 18)
                : Prime5;
            h += (uint)_total;
            int i = 0;
            for (; i + 4 <= _pendingCount; i += 4)
            {
                h = Rotl(h + Lane(_pending, i) * Prime3, 17) * Prime4;
            }
            for (; i < _pendingCount; i++)
            {
                h = Rotl(h + _pending[i] * Prime5, 11) * Prime1;
            }
            h ^= h >> 15;
            h *= Prime2;
            h ^= h >> 13;
            h *= Prime3;
            h ^= h >> 16;
        }
        return h;
    }
}
