namespace Sts2Sim.Core.State;

public enum FidelityFlag : byte
{
    DeferredListenerPromptInline,
    ApproximatedRule
}

public readonly record struct FidelityEntry(FidelityFlag Flag, string Site);

public sealed class FidelityLog : ICanonical
{
    private readonly List<FidelityEntry> _entries;

    public FidelityLog()
    {
        _entries = new List<FidelityEntry>();
    }

    private FidelityLog(List<FidelityEntry> entries)
    {
        _entries = entries;
    }

    public IReadOnlyList<FidelityEntry> Entries => _entries;

    public int Count => _entries.Count;

    public void Record(FidelityFlag flag, string site)
    {
        ArgumentNullException.ThrowIfNull(site);
        _entries.Add(new FidelityEntry(flag, site));
    }

    public bool Contains(FidelityFlag flag)
    {
        var entries = _entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Flag == flag)
            {
                return true;
            }
        }
        return false;
    }

    public bool Contains(FidelityFlag flag, string site)
    {
        var entries = _entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Flag == flag && string.Equals(entries[i].Site, site, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public int CountOf(FidelityFlag flag)
    {
        int n = 0;
        var entries = _entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Flag == flag)
            {
                n++;
            }
        }
        return n;
    }

    public FidelityLog DeepClone() => new(new List<FidelityEntry>(_entries));

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(FidelityCanonicalTags.Log);
        var entries = _entries;
        w.WriteCount(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            w.WriteByte((byte)entries[i].Flag);
            w.WriteString(entries[i].Site);
        }
    }
}
