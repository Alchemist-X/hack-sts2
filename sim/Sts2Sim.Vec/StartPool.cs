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
    private RunSim? _template;

    internal StartEntry(int id, StartKind kind, FreshStart? fresh, string? savePath, RunSim? template)
    {
        Id = id;
        Kind = kind;
        Fresh = fresh;
        SavePath = savePath;
        _template = template;
    }

    public int Id { get; }

    public StartKind Kind { get; }

    public FreshStart? Fresh { get; }

    public string? SavePath { get; }

    /// <summary>
    /// The RunSim every start of this entry clones. Written under <see cref="Gate"/>, read without it: a template is never
    /// stepped, so the clones taken from it in parallel only read it (StartPool.Materialize).
    /// </summary>
    internal RunSim? Template
    {
        get => Volatile.Read(ref _template);
        set => Volatile.Write(ref _template, value);
    }

    /// <summary>True once the entry holds the RunSim its resets clone (fresh entries: after the first start or a warm-up).</summary>
    public bool HasTemplate => Template != null;

    /// <summary>True once the entry was released (<see cref="StartPool.Release"/>, <c>sts2vec_pool_release</c>).</summary>
    public bool Released { get; internal set; }

    /// <summary>
    /// Save entries whose import stops at a prompt (a choice during the load itself): RunSim.Clone shares the pending load
    /// room (Pending.Load) between clones, so these entries re-import the save on every materialisation instead.
    /// </summary>
    internal SavedRun? ReimportSave { get; init; }

    internal ImportOptions? ReimportOptions { get; init; }

    /// <summary>Serialises template creation (and save re-imports) of this entry across worker threads.</summary>
    internal object Gate { get; } = new();
}

/// <summary>
/// Start pool (CONTRACTS.md §D4; owner: V2). Entry ids are issued in insertion order (0, 1, …) and never reused.
/// <see cref="Release"/> removes entries for good (their templates become garbage); <see cref="Count"/> is the number of
/// live entries and the start sequence (<see cref="SequenceEntry"/>) runs over the live entries in id order, so with
/// no release the live ids are exactly 0..Count-1 as before. Fresh templates are created on first use (or ahead of time
/// by <see cref="Warm"/>, <c>sts2vec_pool_warm</c>) and cached while fewer than <see cref="CacheLimit"/> templates exist;
/// beyond that a fresh entry runs NewRun on every materialisation. Adds and releases are serialised; Materialize is safe
/// from parallel workers.
/// </summary>
public sealed class StartPool
{
    private readonly Dictionary<int, StartEntry> _live = new();
    private readonly List<int> _order = new();
    private readonly HashSet<int> _releaseScratch = new();
    private readonly object _lock = new();
    private int _nextId;
    private int _cached;

    public StartPool(int cacheLimit)
    {
        CacheLimit = Math.Max(0, cacheLimit);
    }

    public int CacheLimit { get; }

    public int CachedTemplates => Volatile.Read(ref _cached);

    /// <summary>Number of live entries (released entries are gone; <c>sts2vec_pool_count</c>).</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _order.Count;
            }
        }
    }

    /// <summary>Number of ids issued so far (live + released); the next entry gets this id.</summary>
    public int IdsIssued
    {
        get
        {
            lock (_lock)
            {
                return _nextId;
            }
        }
    }

    /// <summary>The live entry <paramref name="id"/>; throws when it was never issued or was released.</summary>
    public StartEntry this[int id]
    {
        get
        {
            lock (_lock)
            {
                return Find(id);
            }
        }
    }

    /// <summary>True when <paramref name="id"/> is a live entry.</summary>
    public bool IsLive(int id)
    {
        lock (_lock)
        {
            return _live.ContainsKey(id);
        }
    }

    /// <summary>Why <paramref name="id"/> cannot start a slot: null for a live entry, else "was released" / "does not exist".</summary>
    public string? Problem(int id)
    {
        lock (_lock)
        {
            return ProblemOf(id);
        }
    }

    /// <summary>Throws unless <paramref name="id"/> is a live entry (ArgumentException naming the problem).</summary>
    public void Require(int id)
    {
        lock (_lock)
        {
            _ = Find(id);
        }
    }

    /// <summary>
    /// The start sequence (CONTRACTS.md §D2): the live entry at position <c>k mod Count</c> in id order, or -1 when the pool
    /// has no live entry. Without releases this is entry <c>k mod Count</c>.
    /// </summary>
    public int SequenceEntry(ulong k)
    {
        lock (_lock)
        {
            int n = _order.Count;
            return n == 0 ? -1 : _order[(int)(k % (ulong)n)];
        }
    }

    /// <summary>Live entry ids in id (= insertion) order.</summary>
    public int[] LiveIds()
    {
        lock (_lock)
        {
            return _order.ToArray();
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
        return Add(id => new StartEntry(id, StartKind.Fresh, start, null, null));
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
        var sim = result.Sim;
        if (sim.HasPendingChoice)
        {
            return Add(id => new StartEntry(id, StartKind.Save, null, path, sim) { ReimportSave = save, ReimportOptions = options });
        }
        return Add(id => new StartEntry(id, StartKind.Save, null, path, sim));
    }

    /// <summary>Adds a live snapshot; the caller passes a clone it no longer mutates.</summary>
    public int AddSnapshot(RunSim clone)
    {
        ArgumentNullException.ThrowIfNull(clone);
        return Add(id => new StartEntry(id, StartKind.Snapshot, null, null, clone));
    }

    /// <summary>
    /// Releases the live entries <paramref name="ids"/>, all or nothing: an id that is not live (never issued, or released
    /// already) or that is listed twice fails the call before anything changes. A released entry leaves <see cref="Count"/>
    /// and the start sequence, drops its template (a cached fresh template also frees its cache slot), and its id is never
    /// issued again: using it later (reset, restore, release) is an error. Slots that started from it keep their own
    /// clone. Returns the number of entries released.
    /// </summary>
    public int Release(ReadOnlySpan<int> ids)
    {
        lock (_lock)
        {
            var seen = _releaseScratch;
            seen.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                if (ProblemOf(ids[i]) is { } why)
                {
                    throw new ArgumentException($"entries[{i}] = {ids[i]}: {why}");
                }
                if (!seen.Add(ids[i]))
                {
                    throw new ArgumentException($"entries[{i}] = {ids[i]} is listed twice");
                }
            }
            seen.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                var e = _live[id];
                _live.Remove(id);
                _order.RemoveAt(_order.BinarySearch(id));
                e.Released = true;
                lock (e.Gate)
                {
                    if (e.Kind == StartKind.Fresh && e.Template != null)
                    {
                        Interlocked.Decrement(ref _cached);
                    }
                    e.Template = null;
                }
            }
            return ids.Length;
        }
    }

    /// <summary>
    /// A fresh RunSim for one episode started from <paramref name="id"/> (a clone of the template). The clone itself runs
    /// outside the entry's lock: a template is never stepped, so parallel starts from one entry clone it concurrently
    /// (RunSim.Clone only reads its source).
    /// </summary>
    public RunSim Materialize(int id)
    {
        var e = this[id];
        if (e.ReimportSave != null)
        {
            lock (e.Gate)
            {
                var again = RunImporter.FromSave(e.ReimportSave, e.ReimportOptions!);
                return again.Sim ?? throw new NotSupportedException($"save '{e.SavePath}' no longer imports: {string.Join("; ", again.UnsupportedReasons)}");
            }
        }
        var template = e.Template;
        if (template == null)
        {
            lock (e.Gate)
            {
                template = e.Template;
                if (template == null)
                {
                    var sim = NewTemplate(e);
                    if (!TryCache(e, sim))
                    {
                        return sim;
                    }
                    template = sim;
                }
            }
        }
        return template.Clone();
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
            for (int i = 0; i < _order.Count && todo.Count < limit; i++)
            {
                var e = _live[_order[i]];
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

    /// <summary>The live entry <paramref name="id"/> (caller holds the lock); throws naming the problem.</summary>
    private StartEntry Find(int id)
    {
        if (_live.TryGetValue(id, out var e))
        {
            return e;
        }
        if (id >= 0 && id < _nextId)
        {
            throw new ArgumentException(ProblemOf(id), "entry");
        }
        throw new ArgumentOutOfRangeException("entry", ProblemOf(id));
    }

    /// <summary>Null for a live entry, else why it cannot be used (caller holds the lock).</summary>
    private string? ProblemOf(int id)
    {
        if (_live.ContainsKey(id))
        {
            return null;
        }
        return id >= 0 && id < _nextId
            ? $"pool entry {id} was released (released ids are never reused)"
            : $"pool entry {id} does not exist (the pool has {_order.Count} entries; {_nextId} ids issued)";
    }

    private int Add(Func<int, StartEntry> create)
    {
        lock (_lock)
        {
            if (_nextId == int.MaxValue)
            {
                throw new InvalidOperationException("the pool has issued int.MaxValue entry ids; ids are never reused, create a new env");
            }
            int id = _nextId;
            var e = create(id);
            _live.Add(id, e);
            _order.Add(id);
            _nextId++;
            return id;
        }
    }
}
