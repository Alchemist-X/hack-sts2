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

    /// <summary>True once the entry holds the RunSim its resets clone (fresh entries: after the first start or a warm-up).</summary>
    public bool HasTemplate => Template != null;

    /// <summary>
    /// Save entries whose import stops at a prompt (a choice during the load itself): RunSim.Clone shares the pending load
    /// room (Pending.Load) between clones, so these entries re-import the save on every materialisation instead.
    /// </summary>
    internal SavedRun? ReimportSave { get; init; }

    internal ImportOptions? ReimportOptions { get; init; }

    /// <summary>Serialises template creation and cloning of this entry across worker threads.</summary>
    internal object Gate { get; } = new();
}

/// <summary>
/// Start pool (CONTRACTS.md §D4; owner: V2). Entries are ids 0..Count-1 in insertion order. Fresh templates are created on
/// first use (or ahead of time by <see cref="Warm"/>, <c>sts2vec_pool_warm</c>) and cached while fewer than
/// <see cref="CacheLimit"/> templates exist; beyond that a fresh entry runs NewRun on every materialisation. Adds are
/// serialised; Materialize is safe from parallel workers.
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
        var options = ImportOptions.Default with { Content = content };
        var result = RunImporter.FromSave(save, options);
        if (!result.IsSupported || result.Sim == null)
        {
            throw new NotSupportedException($"save '{path}' is not supported: {string.Join("; ", result.UnsupportedReasons)}");
        }
        if (result.Sim.HasPendingChoice)
        {
            lock (_lock)
            {
                int id = _entries.Count;
                _entries.Add(new StartEntry(id, StartKind.Save, null, path, result.Sim) { ReimportSave = save, ReimportOptions = options });
                return id;
            }
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
            if (e.ReimportSave != null)
            {
                var again = RunImporter.FromSave(e.ReimportSave, e.ReimportOptions!);
                return again.Sim ?? throw new NotSupportedException($"save '{e.SavePath}' no longer imports: {string.Join("; ", again.UnsupportedReasons)}");
            }
            if (e.Template == null)
            {
                var sim = NewTemplate(e);
                if (!TryCache(e, sim))
                {
                    return sim;
                }
            }
            return e.Template!.Clone();
        }
    }

    /// <summary>
    /// Creates now the templates of up to <paramref name="maxEntries"/> fresh entries that have none yet (all of them when
    /// negative), in id order and only while the cache has room (<see cref="CacheLimit"/>), on up to
    /// <paramref name="threads"/> workers; returns how many were created (0 when every fresh entry is warm or the cache is
    /// full). A warmed template is exactly the one the entry's first <see cref="Materialize"/> would create
    /// (<c>RunSim.NewRun</c> of its config), so warming moves the cost of the cold starts out of reset/step without
    /// changing what any slot starts from. Save and snapshot entries hold their template already. If a NewRun throws, the
    /// first exception propagates; templates created before it stay cached.
    /// </summary>
    public int Warm(int maxEntries, int threads)
    {
        var todo = new List<StartEntry>();
        lock (_lock)
        {
            int room = CacheLimit - CachedTemplates;
            int limit = maxEntries < 0 ? room : Math.Min(maxEntries, room);
            for (int i = 0; i < _entries.Count && todo.Count < limit; i++)
            {
                var e = _entries[i];
                if (e.Kind == StartKind.Fresh && e.Template == null)
                {
                    todo.Add(e);
                }
            }
        }
        int created = 0;
        void WarmOne(StartEntry e)
        {
            lock (e.Gate)
            {
                if (e.Template == null && TryCache(e, NewTemplate(e)))
                {
                    Interlocked.Increment(ref created);
                }
            }
        }
        if (threads <= 1 || todo.Count <= 1)
        {
            foreach (var e in todo)
            {
                WarmOne(e);
            }
            return created;
        }
        try
        {
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = threads }, WarmOne);
        }
        catch (AggregateException ae) when (ae.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ae.InnerExceptions[0]).Throw();
        }
        return created;
    }

    private static RunSim NewTemplate(StartEntry e)
    {
        var fresh = e.Fresh ?? throw new InvalidOperationException($"pool entry {e.Id} ({e.Kind}) has no template");
        return RunSim.NewRun(fresh.ToRunConfig());
    }

    /// <summary>Caches <paramref name="sim"/> as the entry's template if the cache has room (caller holds the entry's gate).</summary>
    private bool TryCache(StartEntry e, RunSim sim)
    {
        if (Interlocked.Increment(ref _cached) <= CacheLimit)
        {
            e.Template = sim;
            return true;
        }
        Interlocked.Decrement(ref _cached);
        return false;
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
