using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

public enum StartKind
{
    Fresh,
    Save,
    Snapshot,
}

/// <summary>A fresh-run start: <c>RunSim.NewRun(new RunConfig(SeedText, Character, Ascension) { Act1, Content })</c>.</summary>
public sealed record FreshStart(string Character, int Ascension, string SeedText, string Act1, ContentFilter Content)
{
    public RunConfig ToRunConfig() => new(SeedText, Character, Ascension) { Act1 = Act1, Content = Content };
}

/// <summary>One pool entry. Template is the RunSim every reset clones (null for an uncached fresh entry).</summary>
public sealed class StartEntry
{
    internal StartEntry(int id, StartKind kind, FreshStart? fresh, string? savePath, RunSim? template)
    {
        Id = id;
        Kind = kind;
        Fresh = fresh;
        SavePath = savePath;
        Template = template;
    }

    public int Id { get; }

    public StartKind Kind { get; }

    public FreshStart? Fresh { get; }

    public string? SavePath { get; }

    internal RunSim? Template { get; set; }

    /// <summary>Serialises template creation and cloning of this entry across worker threads.</summary>
    internal object Gate { get; } = new();
}

/// <summary>
/// Start pool (CONTRACTS.md §D4; owner: V2). Entries are ids 0..Count-1 in insertion order. Fresh templates are created on
/// first use and cached while fewer than <see cref="CacheLimit"/> templates exist; beyond that a fresh entry runs NewRun on
/// every materialisation. Adds are serialised; Materialize is safe from parallel workers.
/// </summary>
public sealed class StartPool
{
    private readonly List<StartEntry> _entries = new();
    private readonly object _lock = new();
    private int _cached;

    public StartPool(int cacheLimit)
    {
        CacheLimit = Math.Max(0, cacheLimit);
    }

    public int CacheLimit { get; }

    public int CachedTemplates => Volatile.Read(ref _cached);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public StartEntry this[int id]
    {
        get
        {
            lock (_lock)
            {
                if ((uint)id >= (uint)_entries.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(id), $"pool entry {id} does not exist (count {_entries.Count})");
                }
                return _entries[id];
            }
        }
    }

    /// <summary>Adds a fresh entry (validated now, created lazily).</summary>
    public int AddFresh(FreshStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (!CharacterRegistry.TryGet(start.Character, out _))
        {
            throw new ArgumentException($"unknown character '{start.Character}' (known: {string.Join(", ", CharacterRegistry.AllIds)})");
        }
        if (start.Ascension is < 0 or > 10)
        {
            throw new ArgumentException($"ascension {start.Ascension} outside 0..10");
        }
        return Add(StartKind.Fresh, start, null, null);
    }

    /// <summary>Imports a save with the checked path (RunImporter.FromSave); unsupported → NotSupportedException with the reasons.</summary>
    public int AddSave(string path, ContentFilter content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var save = RunSaveReader.ReadFile(path);
        var result = RunImporter.FromSave(save, ImportOptions.Default with { Content = content });
        if (!result.IsSupported || result.Sim == null)
        {
            throw new NotSupportedException($"save '{path}' is not supported: {string.Join("; ", result.UnsupportedReasons)}");
        }
        return Add(StartKind.Save, null, path, result.Sim);
    }

    /// <summary>Adds a live snapshot; the caller passes a clone it no longer mutates.</summary>
    public int AddSnapshot(RunSim clone)
    {
        ArgumentNullException.ThrowIfNull(clone);
        return Add(StartKind.Snapshot, null, null, clone);
    }

    /// <summary>A fresh RunSim for one episode started from <paramref name="id"/> (a clone of the template).</summary>
    public RunSim Materialize(int id)
    {
        var e = this[id];
        lock (e.Gate)
        {
            if (e.Template == null)
            {
                var fresh = e.Fresh ?? throw new InvalidOperationException($"pool entry {id} ({e.Kind}) has no template");
                var sim = RunSim.NewRun(fresh.ToRunConfig());
                if (Interlocked.Increment(ref _cached) <= CacheLimit)
                {
                    e.Template = sim;
                }
                else
                {
                    Interlocked.Decrement(ref _cached);
                    return sim;
                }
            }
            return e.Template.Clone();
        }
    }

    private int Add(StartKind kind, FreshStart? fresh, string? path, RunSim? template)
    {
        lock (_lock)
        {
            int id = _entries.Count;
            _entries.Add(new StartEntry(id, kind, fresh, path, template));
            return id;
        }
    }
}
