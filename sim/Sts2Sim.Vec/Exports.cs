using System.Collections.Concurrent;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// The C ABI (CONTRACTS.md §D5). Frozen names and signatures. Every export catches every exception: errors return -1
/// (sts2vec_create: -1 as int64) with the text available from sts2vec_last_error; per-slot problems go to <c>status</c>.
/// Managed method name = PascalCase of the export name without the "sts2vec_" prefix (the hostfxr loader relies on it).
/// Locking: every export that touches an env (except sts2vec_pool_count and sts2vec_last_error, which only read) holds a
/// lock on that env for the whole call, so calls on one env from several threads serialise (a branch issued while
/// another thread steps waits for the step to return, and vice versa); different envs never contend.
/// </summary>
public static unsafe class Exports
{
    public const string Prefix = "sts2vec/";

    /// <summary>
    /// <c>sts2vec_abi_version()</c>. 1 = every build before that export existed (the 14 frozen exports, plus
    /// pool_warm / clone_determinized / floor_step_cap, detected by export presence and the schema as before); 2 = adds
    /// sts2vec_abi_version, sts2vec_branch, sts2vec_branch_many, sts2vec_restore_many, sts2vec_pool_release and released
    /// pool entries.
    /// Bump it with every later ABI addition or semantic change.
    /// </summary>
    public const int CurrentAbiVersion = 2;

    private static readonly ConcurrentDictionary<long, VecEnv> Envs = new();
    private static long _nextHandle;
    private static volatile string? _globalError;

    /// <summary>sts2vec_create refuses to run under Workstation GC unless this is false (tests) or STS2VEC_ALLOW_WORKSTATION_GC=1.</summary>
    public static bool RequireServerGC { get; set; } =
        Environment.GetEnvironmentVariable("STS2VEC_ALLOW_WORKSTATION_GC") != "1";

    public static string VersionString => Prefix + Sts2Sim.Vec.Schema.EncoderVersion;

    /// <summary>The env behind a handle (for in-process tests and tools); null when the handle is unknown.</summary>
    public static VecEnv? EnvOf(long handle) => Envs.TryGetValue(handle, out var env) ? env : null;

    // ----------------------------------------------------------------------------------------------- exports

    // int32_t sts2vec_version(uint8_t* buf, int32_t cap);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_version")]
    public static int Version(byte* buf, int cap)
    {
        try
        {
            return WriteUtf8(VersionString, buf, cap);
        }
        catch (Exception e)
        {
            return Fail(null, e);
        }
    }

    // int32_t sts2vec_schema(uint8_t* buf, int32_t cap);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_schema")]
    public static int Schema(byte* buf, int cap) => SchemaImpl(buf, cap);

    // int64_t sts2vec_create(const uint8_t* config_json, int32_t len);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_create")]
    public static long Create(byte* configJson, int len)
    {
        try
        {
            if (RequireServerGC && !GCSettings.IsServerGC)
            {
                throw new InvalidOperationException(
                    "Server GC is off (GCSettings.IsServerGC == false): host the library with sts2vec.runtimeconfig.json " +
                    "(\"System.GC.Server\": true) or publish with ServerGarbageCollection; Workstation GC caps batched stepping ~5x lower.");
            }
            var config = VecConfig.FromJson(ReadUtf8(configJson, len, "config_json"));
            var env = new VecEnv(config);
            long h = Interlocked.Increment(ref _nextHandle);
            Envs[h] = env;
            return h;
        }
        catch (Exception e)
        {
            Fail(null, e);
            return -1;
        }
    }

    // int32_t sts2vec_destroy(int64_t h);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_destroy")]
    public static int Destroy(long h)
    {
        try
        {
            if (!Envs.TryRemove(h, out _))
            {
                throw new ArgumentException($"invalid handle {h}");
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(null, e);
        }
    }

    // int32_t sts2vec_set_threads(int64_t h, int32_t threads);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_set_threads")]
    public static int SetThreads(long h, int threads)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            lock (env)
            {
                env.Threads = threads;
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_pool_add_fresh(int64_t h, const uint8_t* json, int32_t len);
    // {"character","ascension","seed","act1","content"} → entry id or -1
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_pool_add_fresh")]
    public static int PoolAddFresh(long h, byte* json, int len)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            var start = ParseFresh(ReadUtf8(json, len, "json"), env.Content);
            lock (env)
            {
                return env.Pool.AddFresh(start);
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_pool_add_save(int64_t h, const uint8_t* path_utf8, int32_t len);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_pool_add_save")]
    public static int PoolAddSave(long h, byte* pathUtf8, int len)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            string path = ReadUtf8(pathUtf8, len, "path");
            lock (env)
            {
                return env.Pool.AddSave(path, env.Content);
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_pool_count(int64_t h);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_pool_count")]
    public static int PoolCount(long h)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            return env.Pool.Count;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_pool_warm(int64_t h, int32_t max_entries);   additive (E2c), listed last in ExportNames
    // Creates the templates of up to max_entries fresh entries now (max_entries < 0: all of them), in id order, while the
    // template cache (config.pool_cache) has room, on the env's worker threads. Returns the number created by this call
    // (0 when everything is warm or the cache is full), or -1. Resets then clone the warm templates: same starts, no NewRun.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_pool_warm")]
    public static int PoolWarm(long h, int maxEntries)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            lock (env)
            {
                return env.Pool.Warm(maxEntries, env.Threads);
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_snapshot(int64_t h, int32_t slot);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_snapshot")]
    public static int Snapshot(long h, int slot)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            lock (env)
            {
                return env.Snapshot(slot);
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_clone_determinized(int64_t h, int32_t slot, uint64_t seed);   additive, listed last in ExportNames
    // Appends a pool entry holding a determinized clone of the slot's current state (public state identical, hidden state
    // resampled from seed; RunSim.CloneDeterminized) and returns its id, or -1. sts2vec_restore starts a slot from it.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_clone_determinized")]
    public static int CloneDeterminized(long h, int slot, ulong seed)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            lock (env)
            {
                return env.CloneDeterminized(slot, seed);
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_abi_version(void);   additive (ABI 2)
    // The ABI version of this build (CurrentAbiVersion); a library without the export is ABI 1.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_abi_version")]
    public static int AbiVersion() => CurrentAbiVersion;

    // int32_t sts2vec_branch(int64_t h, int32_t src_slot, const int32_t* dst_slots, int32_t n, int32_t mode, uint64_t seed,
    //                        void** buffers);   additive (ABI 2)
    // In one call, every dst_slots[j] gets a copy of src_slot's live state (mode 0: exact clone; 1: determinized with seed
    // BranchSeed(seed, j)) without a pool entry, starts a new episode (RESET, new episode id, step index 0, start_entry -1,
    // last_episode untouched) and has its observation, candidates and signals written; other slots are untouched. 0 or -1.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_branch")]
    public static int Branch(long h, int srcSlot, int* dstSlots, int n, int mode, ulong seed, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (n < 0 || n > env.NumEnvs || (n > 0 && dstSlots == null))
            {
                throw new ArgumentException($"branch needs 0 <= n <= {env.NumEnvs} and non-null dst_slots when n > 0 (got n = {n})");
            }
            lock (env)
            {
                env.Branch(srcSlot, new ReadOnlySpan<int>(dstSlots, n), (BranchMode)mode, seed, buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_branch_many(int64_t h, const int32_t* src_slots, const int32_t* dst_slots, int32_t n, int32_t mode,
    //                             uint64_t seed, void** buffers);   additive (ABI 2)
    // sts2vec_branch for n (src, dst) pairs in one call (many roots: one parallel region, one encode per source); the
    // destination at position j uses BranchSeed(seed, j). A destination may not be a source of the same call. 0 or -1.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_branch_many")]
    public static int BranchMany(long h, int* srcSlots, int* dstSlots, int n, int mode, ulong seed, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (n < 0 || n > env.NumEnvs || (n > 0 && (srcSlots == null || dstSlots == null)))
            {
                throw new ArgumentException($"branch_many needs 0 <= n <= {env.NumEnvs} and non-null src_slots/dst_slots when n > 0 (got n = {n})");
            }
            lock (env)
            {
                env.BranchMany(new ReadOnlySpan<int>(srcSlots, n), new ReadOnlySpan<int>(dstSlots, n), (BranchMode)mode, seed, buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_restore_many(int64_t h, const int32_t* slots, const int32_t* entries, int32_t n, void** buffers);
    // additive (ABI 2): sts2vec_restore for n (slot, entry) pairs under one lock, in parallel; 0 or -1 (nothing changed).
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_restore_many")]
    public static int RestoreMany(long h, int* slots, int* entries, int n, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (n < 0 || n > env.NumEnvs || (n > 0 && (slots == null || entries == null)))
            {
                throw new ArgumentException($"restore_many needs 0 <= n <= {env.NumEnvs} and non-null slots/entries when n > 0 (got n = {n})");
            }
            lock (env)
            {
                env.RestoreMany(new ReadOnlySpan<int>(slots, n), new ReadOnlySpan<int>(entries, n), buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_pool_release(int64_t h, const int32_t* entries, int32_t n);   additive (ABI 2)
    // Releases n live pool entries (all or nothing): their templates are dropped, pool_count and the start sequence no
    // longer include them, and their ids are never reused (using one is an error). Returns n, or -1 (nothing released).
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_pool_release")]
    public static int PoolRelease(long h, int* entries, int n)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (n < 0 || (n > 0 && entries == null))
            {
                throw new ArgumentException($"pool_release needs n >= 0 and non-null entries when n > 0 (got n = {n})");
            }
            lock (env)
            {
                return env.ReleaseEntries(new ReadOnlySpan<int>(entries, n));
            }
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_restore(int64_t h, int32_t slot, int32_t entry, void** buffers);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_restore")]
    public static int Restore(long h, int slot, int entry, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            lock (env)
            {
                env.Restore(slot, entry, buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_reset(int64_t h, const uint8_t* mask, const int32_t* start_ids, void** buffers);
    // mask == NULL → all slots; start_ids == NULL → every slot from its start sequence.
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_reset")]
    public static int Reset(long h, byte* mask, int* startIds, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            int n = env.NumEnvs;
            lock (env)
            {
                env.Reset(mask == null ? default : new ReadOnlySpan<byte>(mask, n),
                    startIds == null ? default : new ReadOnlySpan<int>(startIds, n), buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_step(int64_t h, const int32_t* actions, void** buffers);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_step")]
    public static int Step(long h, int* actions, void** buffers)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }
            lock (env)
            {
                env.Step(new ReadOnlySpan<int>(actions, env.NumEnvs), buffers);
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_state_hash(int64_t h, const int32_t* slots, int32_t n, uint64_t* out);
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_state_hash")]
    public static int StateHash(long h, int* slots, int n, ulong* output)
    {
        VecEnv? env = null;
        try
        {
            env = Get(h);
            if (n < 0 || (n > 0 && (slots == null || output == null)))
            {
                throw new ArgumentException("state_hash needs n >= 0 and non-null slots/out");
            }
            lock (env)
            {
                for (int i = 0; i < n; i++)
                {
                    output[i] = env.StateHash(slots[i]);
                }
            }
            return 0;
        }
        catch (Exception e)
        {
            return Fail(env, e);
        }
    }

    // int32_t sts2vec_last_error(int64_t h, int32_t slot, uint8_t* buf, int32_t cap);   slot -1 = env-level; h 0 = global
    [UnmanagedCallersOnly(EntryPoint = "sts2vec_last_error")]
    public static int LastError(long h, int slot, byte* buf, int cap)
    {
        try
        {
            string text;
            if (h == 0)
            {
                text = _globalError ?? "";
            }
            else
            {
                var env = Get(h);
                if (slot == -1)
                {
                    text = env.LastError ?? "";
                }
                else if ((uint)slot < (uint)env.NumEnvs)
                {
                    text = env.Slots[slot].LastError ?? "";
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} outside -1..{env.NumEnvs - 1}");
                }
            }
            return WriteUtf8(text, buf, cap);
        }
        catch (Exception e)
        {
            return Fail(null, e);
        }
    }

    // ----------------------------------------------------------------------------------------------- helpers

    /// <summary>
    /// Names of every export, in §D5 order (the Python loader resolves exactly these). The frozen scaffold set comes first;
    /// exports added later are appended (E2c: sts2vec_pool_warm; then sts2vec_clone_determinized; ABI 2:
    /// sts2vec_abi_version, sts2vec_branch, sts2vec_branch_many, sts2vec_restore_many, sts2vec_pool_release).
    /// </summary>
    public static readonly IReadOnlyList<string> ExportNames = new[]
    {
        "sts2vec_version", "sts2vec_schema", "sts2vec_create", "sts2vec_destroy", "sts2vec_set_threads",
        "sts2vec_pool_add_fresh", "sts2vec_pool_add_save", "sts2vec_pool_count", "sts2vec_snapshot", "sts2vec_restore",
        "sts2vec_reset", "sts2vec_step", "sts2vec_state_hash", "sts2vec_last_error",
        "sts2vec_pool_warm", "sts2vec_clone_determinized",
        "sts2vec_abi_version", "sts2vec_branch", "sts2vec_branch_many", "sts2vec_restore_many", "sts2vec_pool_release",
    };

    /// <summary>The managed method behind an export: "sts2vec_pool_add_fresh" → "PoolAddFresh".</summary>
    public static string ManagedNameOf(string export) =>
        string.Concat(export["sts2vec_".Length..].Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    public static string GlobalError => _globalError ?? "";

    private static int SchemaImpl(byte* buf, int cap)
    {
        try
        {
            return WriteUtf8(Sts2Sim.Vec.Schema.ToJson(), buf, cap);
        }
        catch (Exception e)
        {
            return Fail(null, e);
        }
    }

    private static VecEnv Get(long h) =>
        Envs.TryGetValue(h, out var env) ? env : throw new ArgumentException($"invalid handle {h}");

    private static int Fail(VecEnv? env, Exception e)
    {
        string text = $"{e.GetType().Name}: {e.Message}";
        _globalError = text;
        if (env != null)
        {
            env.LastError = text;
        }
        return -1;
    }

    /// <summary>Writes min(cap, len) bytes of the UTF-8 text and returns the full byte length.</summary>
    private static int WriteUtf8(string text, byte* buf, int cap)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (buf != null && cap > 0)
        {
            new ReadOnlySpan<byte>(bytes, 0, Math.Min(cap, bytes.Length)).CopyTo(new Span<byte>(buf, cap));
        }
        return bytes.Length;
    }

    private static string ReadUtf8(byte* p, int len, string what)
    {
        if (len < 0 || (len > 0 && p == null))
        {
            throw new ArgumentException($"{what}: invalid pointer/length");
        }
        return len == 0 ? "" : Encoding.UTF8.GetString(p, len);
    }

    /// <summary>{"character","ascension","seed","act1","content"}; character and seed required.</summary>
    public static FreshStart ParseFresh(string json, ContentFilter defaultContent)
    {
        using var doc = JsonDocument.Parse(json);
        string? character = null, seed = null;
        int ascension = 0;
        string act1 = RunConfig.RandomAct1;
        var content = defaultContent;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            switch (p.Name)
            {
                case "character": character = p.Value.GetString(); break;
                case "ascension": ascension = p.Value.GetInt32(); break;
                case "seed": seed = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText(); break;
                case "act1": act1 = p.Value.GetString() ?? RunConfig.RandomAct1; break;
                case "content": content = VecConfig.ParseContent(p.Value.GetString() ?? ""); break;
                default: throw new ArgumentException($"unknown pool_add_fresh key \"{p.Name}\"");
            }
        }
        if (string.IsNullOrEmpty(character)) throw new ArgumentException("pool_add_fresh: \"character\" is required");
        if (string.IsNullOrEmpty(seed)) throw new ArgumentException("pool_add_fresh: \"seed\" is required");
        return new FreshStart(character, ascension, seed, act1, content);
    }
}
