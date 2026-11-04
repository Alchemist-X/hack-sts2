using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sts2Sim.Core;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>config.pool.fresh (CONTRACTS.md §D4): <c>Count</c> entries, character = Characters[i % n], seed "&lt;SeedPrefix&gt;-&lt;character&gt;-&lt;i&gt;".</summary>
public sealed record FreshPoolSpec(IReadOnlyList<string> Characters, int Ascension = 0, int Count = 4096, string SeedPrefix = "TRAIN", string Act1 = RunConfig.RandomAct1);

/// <summary>How <see cref="VecEnv.Branch"/> copies the source slot's state (<c>sts2vec_branch</c> <c>mode</c>).</summary>
public enum BranchMode
{
    /// <summary>0: an exact copy, <c>RunSim.Clone()</c> (same <c>StateHash</c>; steps exactly like the source).</summary>
    Exact = 0,

    /// <summary>
    /// 1: <c>RunSim.CloneDeterminized(VecEnv.BranchSeed(seed, j))</c> for the destination at position j: the same player view,
    /// hidden state (draw order, RNG streams, run queues) resampled per destination.
    /// </summary>
    Determinized = 1,
}

/// <summary>
/// Env configuration (§D5 config_json). JSON keys are snake_case; unknown keys are rejected.
/// <c>FloorStepCap</c> (<c>floor_step_cap</c>, appended): truncate an episode after this many non-combat decisions on one
/// floor without the floor advancing (status FLOOR_STALL, end_kind FloorStall); 0 disables. Same rule and default as the
/// protocol evaluator's <c>stuck</c> limit (docs/sts2-bench-protocol-v0.2-2026-11-01.md §5).
/// <c>Macros</c> (<c>macros</c>, appended W3 2026-11-04): the room macro candidates to list (<see cref="RoomMacros"/>), a JSON
/// array of names (<c>["rest", "shop"]</c>) or one comma-separated string; absent / empty = none (the candidate lists of
/// every earlier build).
/// </summary>
public sealed record VecConfig(
    int NumEnvs,
    int Threads = 8,
    int StepCap = 3000,
    bool AutoReset = true,
    string SeedText = "VEC",
    string Content = "ported_only",
    FreshPoolSpec? Pool = null,
    int PoolCache = 4096,
    bool FastPath = true,
    int FloorStepCap = VecConfig.DefaultFloorStepCap,
    RoomMacroSet Macros = RoomMacroSet.None)
{
    /// <summary>Default <c>floor_step_cap</c>: the protocol's 80 non-combat decisions per floor (Python: <c>sts2vec.FLOOR_STEP_CAP</c>).</summary>
    public const int DefaultFloorStepCap = 80;

    public ContentFilter ContentFilter => ParseContent(Content);

    public static ContentFilter ParseContent(string content) => content switch
    {
        "ported_only" => ContentFilter.PortedOnly,
        "all" => ContentFilter.All,
        _ => throw new ArgumentException($"content must be \"ported_only\" or \"all\", not \"{content}\""),
    };

    public static VecConfig FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("config must be a JSON object");
        }
        int? numEnvs = null;
        var c = new VecConfig(1);
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "num_envs": numEnvs = p.Value.GetInt32(); break;
                case "threads": c = c with { Threads = p.Value.GetInt32() }; break;
                case "step_cap": c = c with { StepCap = p.Value.GetInt32() }; break;
                case "auto_reset": c = c with { AutoReset = p.Value.GetBoolean() }; break;
                case "seed_text": c = c with { SeedText = p.Value.GetString() ?? "" }; break;
                case "content": c = c with { Content = p.Value.GetString() ?? "" }; break;
                case "pool_cache": c = c with { PoolCache = p.Value.GetInt32() }; break;
                case "fast_path": c = c with { FastPath = p.Value.GetBoolean() }; break;
                case "floor_step_cap": c = c with { FloorStepCap = p.Value.GetInt32() }; break;
                case "macros": c = c with { Macros = ParseMacros(p.Value) }; break;
                case "pool": c = c with { Pool = p.Value.ValueKind == JsonValueKind.Null ? null : ParsePool(p.Value) }; break;
                default: throw new ArgumentException($"unknown config key \"{p.Name}\"");
            }
        }
        if (numEnvs == null)
        {
            throw new ArgumentException("config.num_envs is required");
        }
        c = c with { NumEnvs = numEnvs.Value };
        c.Validate();
        return c;
    }

    public void Validate()
    {
        if (NumEnvs < 1) throw new ArgumentException($"num_envs must be >= 1 (got {NumEnvs})");
        if (Threads < 1) throw new ArgumentException($"threads must be >= 1 (got {Threads})");
        if (StepCap < 1) throw new ArgumentException($"step_cap must be >= 1 (got {StepCap})");
        if (PoolCache < 0) throw new ArgumentException($"pool_cache must be >= 0 (got {PoolCache})");
        if (FloorStepCap < 0) throw new ArgumentException($"floor_step_cap must be >= 0 (0 = off; got {FloorStepCap})");
        _ = ContentFilter;
        if (Pool != null)
        {
            if (Pool.Characters.Count == 0) throw new ArgumentException("pool.fresh.characters is empty");
            if (Pool.Count < 0) throw new ArgumentException("pool.fresh.count must be >= 0");
        }
    }

    private static RoomMacroSet ParseMacros(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => RoomMacroSet.None,
        JsonValueKind.String => RoomMacros.Parse((v.GetString() ?? "").Split(',')),
        JsonValueKind.Array => RoomMacros.Parse(v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? "" : throw new ArgumentException("config.macros entries must be strings"))),
        _ => throw new ArgumentException("config.macros must be an array of names (\"rest\", \"shop\") or a comma-separated string"),
    };

    private static FreshPoolSpec ParsePool(JsonElement pool)
    {
        FreshPoolSpec? fresh = null;
        foreach (var p in pool.EnumerateObject())
        {
            if (p.Name != "fresh")
            {
                throw new ArgumentException($"unknown config.pool key \"{p.Name}\"");
            }
            var chars = new List<string>();
            var f = new FreshPoolSpec(chars);
            foreach (var q in p.Value.EnumerateObject())
            {
                switch (q.Name)
                {
                    case "characters":
                        foreach (var ch in q.Value.EnumerateArray())
                        {
                            chars.Add(ch.GetString() ?? "");
                        }
                        break;
                    case "ascension": f = f with { Ascension = q.Value.GetInt32() }; break;
                    case "count": f = f with { Count = q.Value.GetInt32() }; break;
                    case "seed_prefix": f = f with { SeedPrefix = q.Value.GetString() ?? "" }; break;
                    case "act1": f = f with { Act1 = q.Value.GetString() ?? RunConfig.RandomAct1 }; break;
                    default: throw new ArgumentException($"unknown config.pool.fresh key \"{q.Name}\"");
                }
            }
            fresh = f;
        }
        return fresh ?? throw new ArgumentException("config.pool needs a \"fresh\" object");
    }
}

/// <summary>One env slot (§D2).</summary>
public sealed class VecSlot
{
    internal VecSlot(int index, ulong seed)
    {
        Index = index;
        SlotSeed = seed;
    }

    public int Index { get; }

    public RunSim? Sim { get; internal set; }

    /// <summary>episode_id = episodes_started_before * N + slot + 1: unique per env, increasing per slot, scheduling-independent.</summary>
    public long EpisodeId { get; internal set; }

    /// <summary>Number of episodes started on this slot (drives the deterministic start sequence).</summary>
    public long EpisodeCount { get; internal set; }

    public int StepIndex { get; internal set; }

    public int StartEntry { get; internal set; } = -1;

    /// <summary>XXH64(UTF-8(seed_text) ‖ int32 LE slot).</summary>
    public ulong SlotSeed { get; }

    public string? LastError { get; internal set; }

    public SlotStatus Status { get; internal set; }

    /// <summary>auto_reset = false and the episode ended: the slot stays terminal (cand_n = 0) until reset.</summary>
    public bool Finished { get; internal set; }

    /// <summary>Steps taken on the clone-free path (RunSim.StepFast) / through the cloning tape path, over the slot's lifetime.</summary>
    public long FastSteps { get; internal set; }

    public long SlowSteps { get; internal set; }

    /// <summary>Number of selectable candidates published in the last observation.</summary>
    public int CandN { get; internal set; }

    /// <summary>Floor stall cap: <c>RunState.TotalFloor</c> of the slot's current state (-1 = no run).</summary>
    public int FloorSeen { get; internal set; } = -1;

    /// <summary>
    /// Floor stall cap: decisions (actions &gt;= 0, legal or illegal) taken at non-combat decision points since the floor
    /// last changed or the episode started.
    /// </summary>
    public int FloorSteps { get; internal set; }

    /// <summary>
    /// The Vec candidate list at the last observation; candidate i = Legal[i]: <see cref="Raw"/> with the rewards-screen
    /// macro substitutions (<see cref="RewardMacros.Fill"/>), elsewhere equal to it.
    /// </summary>
    internal List<RunAction> Legal { get; } = new(64);

    /// <summary>RunSim.LegalActions() at the last observation (scratch for <see cref="Legal"/>).</summary>
    internal List<RunAction> Raw { get; } = new(64);
}

/// <summary>
/// Batched environment (CONTRACTS.md §D2; owner of the game-logic bodies: V2). All calls are batch calls over the slots;
/// buffers are caller-owned (<see cref="Schema.Buffers"/> order). Per-slot problems go to <c>status</c>; methods throw only
/// on invalid arguments (the exports turn that into -1 + last_error). Not thread-safe: one call at a time (the exports
/// serialise every call on an env with a lock on it); each call fans out over the slots on its own worker threads.
/// </summary>
public sealed unsafe class VecEnv
{
    /// <summary>
    /// The buffers a branch copies from its first destination to the others: every buffer except the per-slot signals
    /// and bookkeeping (reward, terminated, truncated, status, episode, last_episode), i.e. everything
    /// <c>Observe</c> writes (the observation tables, cand, cand_n), including buffers appended later.
    /// </summary>
    private static readonly int[] BranchCopy = Enumerable.Range(0, Buf.Count)
        .Where(k => k is not (Buf.Reward or Buf.Terminated or Buf.Truncated or Buf.Status or Buf.Episode or Buf.LastEpisode))
        .ToArray();

    private readonly int[] _actions;
    private readonly byte[] _mask;
    private readonly int[] _starts;
    private readonly int[] _pairSlots;
    private readonly int[] _pairEntries;
    private readonly int[] _pairSources;
    private readonly int[] _pairRef;
    private readonly int[] _pairOthers;
    private readonly int[] _refOfSource;
    private readonly bool[] _refOk;
    private readonly bool[] _seen;
    private readonly bool[] _isSource;
    private readonly SlotStatus[] _pairStatus;
    private int _threads;

    public VecEnv(VecConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        Config = config;
        Content = config.ContentFilter;
        NumEnvs = config.NumEnvs;
        _threads = config.Threads;
        Pool = new StartPool(config.PoolCache);
        Slots = new VecSlot[NumEnvs];
        for (int i = 0; i < NumEnvs; i++)
        {
            Slots[i] = new VecSlot(i, SlotSeedOf(config.SeedText, i));
        }
        _actions = new int[NumEnvs];
        _mask = new byte[NumEnvs];
        _starts = new int[NumEnvs];
        _pairSlots = new int[NumEnvs];
        _pairEntries = new int[NumEnvs];
        _pairSources = new int[NumEnvs];
        _pairRef = new int[NumEnvs];
        _pairOthers = new int[NumEnvs];
        _refOfSource = new int[NumEnvs];
        Array.Fill(_refOfSource, -1);
        _refOk = new bool[NumEnvs];
        _seen = new bool[NumEnvs];
        _isSource = new bool[NumEnvs];
        _pairStatus = new SlotStatus[NumEnvs];
        if (config.Pool is { } fresh)
        {
            for (int i = 0; i < fresh.Count; i++)
            {
                string ch = fresh.Characters[i % fresh.Characters.Count];
                Pool.AddFresh(new FreshStart(ch, fresh.Ascension, $"{fresh.SeedPrefix}-{ch}-{i}", fresh.Act1, Content));
            }
        }
    }

    public VecConfig Config { get; }

    public ContentFilter Content { get; }

    public int NumEnvs { get; }

    public StartPool Pool { get; }

    public VecSlot[] Slots { get; }

    /// <summary>Env-level last error (slot -1).</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Test/debug hook: called with (slot, action) right before <c>RunSim.Step</c>; a returned exception is thrown in place of
    /// the step, exactly as if the sim had thrown it (CONTENT_GAP / SIM_ERROR classification and auto-reset apply).
    /// Must be thread-safe (it runs on the worker threads). Null in production.
    /// </summary>
    public Func<int, RunAction, Exception?>? FaultInjector { get; set; }

    /// <summary>
    /// Test hook: when it returns true for (slot, action) the step runs clone-free without consulting
    /// <c>RunSim.MayPrompt</c> (exercises the fail-closed path). Thread-safe; null in production.
    /// </summary>
    public Func<int, RunAction, bool>? ForceFastPath { get; set; }

    /// <summary>Sum of <see cref="VecSlot.FastSteps"/> over the slots.</summary>
    public long FastSteps
    {
        get
        {
            long n = 0;
            foreach (var s in Slots)
            {
                n += s.FastSteps;
            }
            return n;
        }
    }

    /// <summary>Sum of <see cref="VecSlot.SlowSteps"/> over the slots.</summary>
    public long SlowSteps
    {
        get
        {
            long n = 0;
            foreach (var s in Slots)
            {
                n += s.SlowSteps;
            }
            return n;
        }
    }

    public int Threads
    {
        get => _threads;
        set => _threads = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "threads must be >= 1");
    }

    public static ulong SlotSeedOf(string seedText, int slot)
    {
        int n = Encoding.UTF8.GetByteCount(seedText);
        var bytes = new byte[n + 4];
        Encoding.UTF8.GetBytes(seedText, bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(n), slot);
        return DeterministicHash.XxHash64(bytes);
    }

    // ------------------------------------------------------------------ batch calls

    /// <summary>
    /// Restarts every slot with mask[i] != 0 from pool entry startIds[i] (or its start sequence when &lt; 0) and writes those
    /// slots' observations and signals; other slots' buffers are untouched. Empty spans mean "all slots" / "all -1".
    /// A masked-in start id that is not a live entry (never issued, or released) fails the call before anything changes.
    /// </summary>
    public void Reset(ReadOnlySpan<byte> mask, ReadOnlySpan<int> startIds, void** buffers)
    {
        RequireBuffers(buffers);
        if (mask.Length != 0 && mask.Length != NumEnvs) throw new ArgumentException($"mask has {mask.Length} entries, expected {NumEnvs}");
        if (startIds.Length != 0 && startIds.Length != NumEnvs) throw new ArgumentException($"start_ids has {startIds.Length} entries, expected {NumEnvs}");
        for (int i = 0; i < NumEnvs; i++)
        {
            _mask[i] = mask.Length == 0 ? (byte)1 : mask[i];
            _starts[i] = startIds.Length == 0 ? -1 : startIds[i];
            if (_mask[i] != 0 && _starts[i] >= 0 && Pool.Problem(_starts[i]) is { } why)
            {
                throw new ArgumentOutOfRangeException(nameof(startIds), $"start_ids[{i}] = {_starts[i]}: {why}");
            }
        }
        nint table = (nint)buffers;
        ForEachSlot(i =>
        {
            if (_mask[i] != 0)
            {
                ResetSlot(i, _starts[i], (void**)table);
            }
        });
    }

    /// <summary>Applies actions[i] (candidate index; &lt; 0 = no-op) to every slot, then writes every slot's observation and signals.</summary>
    public void Step(ReadOnlySpan<int> actions, void** buffers)
    {
        RequireBuffers(buffers);
        if (actions.Length != NumEnvs) throw new ArgumentException($"actions has {actions.Length} entries, expected {NumEnvs}");
        actions.CopyTo(_actions);
        nint table = (nint)buffers;
        ForEachSlot(i => StepSlot(i, _actions[i], (void**)table));
    }

    /// <summary>Resets one slot from one live pool entry (E4 search) and writes that slot's observation.</summary>
    public void Restore(int slot, int entry, void** buffers)
    {
        RequireBuffers(buffers);
        RequireSlot(slot);
        Pool.Require(entry);
        ResetSlot(slot, entry, buffers);
    }

    /// <summary>
    /// Batched <see cref="Restore"/> (<c>sts2vec_restore_many</c>): restarts slot <c>slots[j]</c> from pool entry
    /// <c>entries[j]</c> for every j, in parallel on the env's threads, and writes those slots' observations and signals
    /// (status RESET, a new episode id, step index 0, start_entry = the entry; last_episode untouched). Other slots are
    /// untouched. Validated before anything changes: equal lengths, every slot in range and listed once, every entry live.
    /// The slots are distinct, so the result equals restoring the pairs one by one in any order.
    /// </summary>
    public void RestoreMany(ReadOnlySpan<int> slots, ReadOnlySpan<int> entries, void** buffers)
    {
        RequireBuffers(buffers);
        if (slots.Length != entries.Length)
        {
            throw new ArgumentException($"slots has {slots.Length} entries but entries has {entries.Length}");
        }
        RequireDistinctSlots(slots, "slots", null);
        for (int j = 0; j < entries.Length; j++)
        {
            if (Pool.Problem(entries[j]) is { } why)
            {
                throw new ArgumentException($"entries[{j}] = {entries[j]}: {why}");
            }
        }
        int n = slots.Length;
        slots.CopyTo(_pairSlots);
        entries.CopyTo(_pairEntries);
        nint table = (nint)buffers;
        ForEach(n, j => ResetSlot(_pairSlots[j], _pairEntries[j], (void**)table));
    }

    /// <summary>
    /// Branches slot <paramref name="src"/> into the slots <paramref name="dsts"/> (<c>sts2vec_branch</c>): destination j gets
    /// its own copy of the source's live state, pending prompts included (<see cref="BranchMode.Exact"/>:
    /// <c>RunSim.Clone()</c>; <see cref="BranchMode.Determinized"/>: <c>RunSim.CloneDeterminized(BranchSeed(seed, j))</c>),
    /// without a pool entry, and starts a new episode on it: status RESET, a new episode id, step index 0, start_entry -1
    /// (no pool entry holds the start), floor stall count 0; last_episode untouched. The destinations' observations,
    /// candidates and signals are written in this call, as <see cref="Reset"/> writes the slots it restarts; the source
    /// slot and every other slot are untouched (state and buffer rows). Same as <see cref="BranchMany"/> with
    /// <paramref name="src"/> repeated for every destination.
    /// </summary>
    public void Branch(int src, ReadOnlySpan<int> dsts, BranchMode mode, ulong seed, void** buffers)
    {
        RequireBuffers(buffers);
        RequireSlot(src);
        if (Slots[src].Sim == null)
        {
            throw new InvalidOperationException($"slot {src} holds no run");
        }
        if (dsts.Length > NumEnvs)
        {
            throw new ArgumentException($"dst_slots has {dsts.Length} entries but the env has {NumEnvs} slots");
        }
        Array.Fill(_pairSources, src, 0, dsts.Length);
        dsts.CopyTo(_pairSlots);
        BranchPairs(dsts.Length, mode, seed, buffers);
    }

    /// <summary>
    /// Batched <see cref="Branch"/> (<c>sts2vec_branch_many</c>): destination <c>dsts[j]</c> gets a copy of the live state of
    /// <c>srcs[j]</c> (exact, or determinized with <c>BranchSeed(seed, j)</c>, j = the position in this call), with the same
    /// per-destination result as <see cref="Branch"/>. One call branches many roots: every clone of the call runs in one
    /// parallel region, and each distinct source's view is encoded once. Validated before anything changes: equal
    /// lengths, every source in range and holding a run, every destination in range, listed once and not a source of
    /// this call.
    /// <para>The clones run in parallel on the env's threads (<c>RunSim.Clone</c> only reads its source; several clones of
    /// one source run concurrently). The observation of each source is encoded once, for its first destination (on that
    /// destination's own clone), and copied to the source's other destinations: exact for exact clones and, for
    /// determinized clones, by their player-view contract (identical observation bytes; sim/VEC.md "Determinized
    /// clones"). A clone that throws (not expected) is a per-slot SIM_ERROR / CONTENT_GAP.</para>
    /// </summary>
    public void BranchMany(ReadOnlySpan<int> srcs, ReadOnlySpan<int> dsts, BranchMode mode, ulong seed, void** buffers)
    {
        RequireBuffers(buffers);
        if (srcs.Length != dsts.Length)
        {
            throw new ArgumentException($"src_slots has {srcs.Length} entries but dst_slots has {dsts.Length}");
        }
        if (dsts.Length > NumEnvs)
        {
            throw new ArgumentException($"dst_slots has {dsts.Length} entries but the env has {NumEnvs} slots");
        }
        srcs.CopyTo(_pairSources);
        dsts.CopyTo(_pairSlots);
        BranchPairs(dsts.Length, mode, seed, buffers);
    }

    /// <summary>
    /// The branch of <c>_pairSources[j]</c> into <c>_pairSlots[j]</c> for j &lt; n (both filled by the caller):
    /// validation, then phase 1 (parallel) clones every destination and encodes each source's first destination, then
    /// phase 2 copies those reference views to the other destinations.
    /// </summary>
    private void BranchPairs(int n, BranchMode mode, ulong seed, void** buffers)
    {
        if (mode is not (BranchMode.Exact or BranchMode.Determinized))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), $"mode must be 0 (exact) or 1 (determinized), got {(int)mode}");
        }
        int others = 0;
        try
        {
            for (int j = 0; j < n; j++)
            {
                int src = _pairSources[j];
                if ((uint)src >= (uint)NumEnvs)
                {
                    throw new ArgumentOutOfRangeException("src_slots", $"src_slots[{j}] = {src} outside 0..{NumEnvs - 1}");
                }
                var sim = Slots[src].Sim ?? throw new InvalidOperationException($"src_slots[{j}]: slot {src} holds no run");
                if (sim.IsPoisoned)
                {
                    throw new InvalidOperationException($"src_slots[{j}]: slot {src} holds a poisoned run");
                }
                _isSource[src] = true;
            }
            RequireDistinctSlots(_pairSlots.AsSpan(0, n), "dst_slots", _isSource);
            // the reference destination of each source: its first destination in call order
            for (int j = 0; j < n; j++)
            {
                int src = _pairSources[j];
                if (_refOfSource[src] < 0)
                {
                    _refOfSource[src] = j;
                }
                else
                {
                    _pairOthers[others++] = j;
                }
                _pairRef[j] = _refOfSource[src];
            }
        }
        finally
        {
            for (int j = 0; j < n; j++)
            {
                int src = _pairSources[j];
                if ((uint)src < (uint)NumEnvs)
                {
                    _isSource[src] = false;
                    _refOfSource[src] = -1;
                }
            }
        }
        if (n == 0)
        {
            return;
        }
        nint table = (nint)buffers;
        bool determinized = mode == BranchMode.Determinized;
        // phase 1: every destination clones its source; a reference destination is also encoded (its source's view)
        ForEach(n, j =>
        {
            var s = Slots[_pairSlots[j]];
            var source = Slots[_pairSources[j]].Sim!;
            var st = SlotStatus.Reset;
            BeginBranchEpisode(s);
            try
            {
                var sim = determinized ? source.CloneDeterminized(BranchSeed(seed, j)) : source.Clone();
                s.Sim = sim;
                s.FloorSeen = sim.State.TotalFloor;
            }
            catch (Exception e)
            {
                st |= StatusClassifier.Classify(e);
                s.LastError = e.ToString();
                s.Sim = null;
            }
            if (_pairRef[j] == j)
            {
                var b = new SlotBuffers((void**)table, s.Index);
                bool cloned = s.Sim != null;
                try
                {
                    if (!Observe(s, b, ref st))
                    {
                        s.Sim = null;
                    }
                }
                catch (Exception e)
                {
                    Fault(s, b, e, ref st);
                }
                _refOk[j] = cloned && s.Sim != null;
                WriteSignals(s, b, 0f, 0, 0, st);
            }
            _pairStatus[j] = st;
        });
        // phase 2: every other destination takes its reference's view (rows, candidates, legal list); a destination whose
        // clone failed, or whose reference failed, is encoded on its own
        void Copy(int k)
        {
            int j = _pairOthers[k];
            int r = _pairRef[j];
            var s = Slots[_pairSlots[j]];
            var b = new SlotBuffers((void**)table, s.Index);
            var st = _pairStatus[j];
            try
            {
                if (s.Sim != null && _refOk[r])
                {
                    var from = Slots[_pairSlots[r]];
                    CopyBranchView((void**)table, from.Index, s.Index);
                    s.Legal.Clear();
                    s.Legal.AddRange(from.Legal);
                    s.CandN = from.CandN;
                    st |= from.Status & (SlotStatus.TableOverflow | SlotStatus.CandidateOverflow);
                }
                else if (!Observe(s, b, ref st))
                {
                    s.Sim = null;
                }
            }
            catch (Exception e)
            {
                Fault(s, b, e, ref st);
            }
            WriteSignals(s, b, 0f, 0, 0, st);
        }
        if (others <= BranchSerialCopies)
        {
            for (int k = 0; k < others; k++)
            {
                Copy(k);
            }
        }
        else
        {
            ForEach(others, Copy);
        }
    }

    /// <summary>Phase 2 of a branch copies serially up to this many rows (a ≈ 35 KB copy costs less than waking workers).</summary>
    private const int BranchSerialCopies = 2;

    /// <summary>
    /// The determinization seed of the destination at position <paramref name="index"/> of a <see cref="Branch"/> call with
    /// <paramref name="seed"/>: the SplitMix64 finaliser of <c>seed + (index + 1) * 0x9E3779B97F4A7C15</c> (mod 2^64).
    /// Position-based, so a branch is reproducible whichever slots it writes; Python: <c>sts2vec.branch_seed</c>.
    /// </summary>
    public static ulong BranchSeed(ulong seed, int index)
    {
        ulong z = unchecked(seed + 0x9E3779B97F4A7C15UL * (ulong)(index + 1));
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>Releases pool entries (<c>sts2vec_pool_release</c>, <see cref="StartPool.Release"/>); returns the number released.</summary>
    public int ReleaseEntries(ReadOnlySpan<int> ids) => Pool.Release(ids);

    /// <summary>Adds a pool entry holding a Clone() of the slot's current state (prompts included); returns its id.</summary>
    public int Snapshot(int slot)
    {
        RequireSlot(slot);
        var sim = Slots[slot].Sim ?? throw new InvalidOperationException($"slot {slot} holds no run");
        return Pool.AddSnapshot(sim.Clone());
    }

    /// <summary>
    /// Adds a pool entry holding <c>RunSim.CloneDeterminized(seed)</c> of the slot's current state (prompts included):
    /// the same player view, hidden state (draw order, RNG streams, run queues) resampled from <paramref name="seed"/>.
    /// Returns its id; <see cref="Restore"/> starts a slot from it.
    /// </summary>
    public int CloneDeterminized(int slot, ulong seed)
    {
        RequireSlot(slot);
        var sim = Slots[slot].Sim ?? throw new InvalidOperationException($"slot {slot} holds no run");
        return Pool.AddSnapshot(sim.CloneDeterminized(seed));
    }

    /// <summary>RunSim.StateHash of the slot (0 when it holds no run). Debug/test only.</summary>
    public ulong StateHash(int slot)
    {
        RequireSlot(slot);
        return Slots[slot].Sim?.StateHash() ?? 0UL;
    }

    // ------------------------------------------------------------------ per-slot logic (one thread per slot per call)

    private void ResetSlot(int i, int entry, void** buffers)
    {
        var s = Slots[i];
        var b = new SlotBuffers(buffers, i);
        var st = SlotStatus.Ok;
        try
        {
            StartSlot(s, entry, ref st);
            if (!Observe(s, b, ref st))
            {
                s.Sim = null;
            }
        }
        catch (Exception e)
        {
            Fault(s, b, e, ref st);
        }
        WriteSignals(s, b, 0f, 0, 0, st);
    }

    private void StepSlot(int i, int action, void** buffers)
    {
        var s = Slots[i];
        var b = new SlotBuffers(buffers, i);
        var st = SlotStatus.Ok;
        float reward = 0f;
        byte terminated = 0, truncated = 0;
        try
        {
            var end = EndKind.None;
            if (action < 0)
            {
                st |= SlotStatus.Noop;
            }
            else if (s.Sim == null || s.Finished)
            {
                st |= SlotStatus.IllegalAction;
            }
            else if (action >= s.CandN)
            {
                // The slot is unchanged, but the decision was taken: it counts toward the floor stall cap (as in the
                // protocol evaluator), so a policy stuck on illegal indices at a non-combat point is truncated too.
                st |= SlotStatus.IllegalAction;
                if (!InCombat(s.Sim))
                {
                    s.FloorSteps++;
                }
                if (FloorStalled(s))
                {
                    end = EndKind.FloorStall;
                    truncated = 1;
                    st |= SlotStatus.FloorStall;
                }
            }
            else
            {
                try
                {
                    bool nonCombat = !InCombat(s.Sim);
                    var act = s.Legal[action];
                    if (Config.Macros != RoomMacroSet.None && RoomMacros.IsMacro(act))
                    {
                        // one decision, the game's three clicks: open the SMITH / removal prompt, pick the card, confirm
                        RoomMacros.Execute(s.Sim, act, a => Apply(s, i, a));
                    }
                    else if (RewardMacros.IsMacro(act.Kind))
                    {
                        // one decision, the game's two clicks: open the card reward, then take the card / alternative
                        var (open, choose) = RewardMacros.Expand(act);
                        Apply(s, i, open);
                        RewardMacros.RequireOpened(s.Sim, act);
                        Apply(s, i, choose);
                    }
                    else
                    {
                        Apply(s, i, act);
                    }
                    s.StepIndex++;
                    if (nonCombat)
                    {
                        s.FloorSteps++;
                    }
                    int floor = s.Sim.State.TotalFloor;
                    if (floor != s.FloorSeen)
                    {
                        s.FloorSeen = floor;
                        s.FloorSteps = 0;
                    }
                    if (s.Sim.IsTerminal)
                    {
                        end = EndKind.Terminal;
                        terminated = 1;
                        reward = s.Sim.Loop.Phase == RunPhase.Victory ? 1f : 0f;
                    }
                    else if (s.StepIndex >= Config.StepCap)
                    {
                        end = EndKind.Truncated;
                        truncated = 1;
                    }
                    else if (FloorStalled(s))
                    {
                        end = EndKind.FloorStall;
                        truncated = 1;
                        st |= SlotStatus.FloorStall;
                    }
                }
                catch (Exception e)
                {
                    var bit = StatusClassifier.Classify(e);
                    st |= bit;
                    end = StatusClassifier.EndKindOf(bit);
                    truncated = 1;
                    s.LastError = e.ToString();
                }
            }

            if (end != EndKind.None)
            {
                FinishEpisode(s, b, end, reward, ref st);
            }
            else if (s.Sim == null && s.EpisodeCount > 0 && Config.AutoReset)
            {
                // A slot whose start failed earlier: try the next entry of its start sequence.
                StartSlot(s, -1, ref st);
            }

            if (!Observe(s, b, ref st))
            {
                if (end == EndKind.None && s.Sim != null)
                {
                    // The encoder failed on a live state: end the episode as an error, then observe the restart once.
                    var bit = (st & SlotStatus.ContentGap) != 0 ? SlotStatus.ContentGap : SlotStatus.SimError;
                    truncated = 1;
                    FinishEpisode(s, b, StatusClassifier.EndKindOf(bit), 0f, ref st);
                    if (!Observe(s, b, ref st))
                    {
                        s.Sim = null;
                    }
                }
                else
                {
                    s.Sim = null;
                }
            }
        }
        catch (Exception e)
        {
            Fault(s, b, e, ref st);
        }
        WriteSignals(s, b, reward, terminated, truncated, st);
    }

    /// <summary>Runs one RunSim action on the slot (fault injection, fast path or the cloning tape path; counts the path).</summary>
    private void Apply(VecSlot s, int i, RunAction act)
    {
        var sim = s.Sim!;
        if (FaultInjector?.Invoke(i, act) is { } injected)
        {
            throw injected;
        }
        if (ForceFastPath?.Invoke(i, act) == true)
        {
            s.FastSteps++;
            sim.StepFastUnchecked(act);
        }
        else if (Config.FastPath && sim.StepFast(act))
        {
            s.FastSteps++;
        }
        else
        {
            if (!Config.FastPath)
            {
                sim.Step(act);
            }
            s.SlowSteps++;
        }
    }

    /// <summary>Starts a new episode on the slot from <paramref name="entry"/> or (entry &lt; 0) the slot's start sequence.</summary>
    private void StartSlot(VecSlot s, int entry, ref SlotStatus st)
    {
        s.Sim = null;
        s.Finished = false;
        s.CandN = 0;
        s.Legal.Clear();
        s.Raw.Clear();
        s.StepIndex = 0;
        s.FloorSeen = -1;
        s.FloorSteps = 0;
        st |= SlotStatus.Reset;
        if (entry < 0)
        {
            entry = Pool.SequenceEntry(s.SlotSeed + (ulong)s.EpisodeCount);
            if (entry < 0)
            {
                st |= SlotStatus.PoolEmpty;
                s.StartEntry = -1;
                return;
            }
        }
        s.EpisodeId = s.EpisodeCount * NumEnvs + s.Index + 1;
        s.EpisodeCount++;
        s.StartEntry = entry;
        try
        {
            s.Sim = Pool.Materialize(entry);
            s.FloorSeen = s.Sim.State.TotalFloor;
        }
        catch (Exception e)
        {
            st |= StatusClassifier.Classify(e);
            s.LastError = e.ToString();
            s.Sim = null;
        }
    }

    /// <summary>Writes last_episode for the episode that just ended, then auto-resets (or parks the slot as finished).</summary>
    private void FinishEpisode(VecSlot s, SlotBuffers b, EndKind end, float reward, ref SlotStatus st)
    {
        WriteLastEpisode(s, b, end, reward);
        if (end is EndKind.ContentGap or EndKind.SimError)
        {
            s.Sim = null;
        }
        if (Config.AutoReset)
        {
            StartSlot(s, -1, ref st);
        }
        else
        {
            s.Finished = true;
        }
    }

    /// <summary>Encodes the slot's observation and candidates. False when the encoder threw (status bit + last error set, tables zeroed).</summary>
    private bool Observe(VecSlot s, SlotBuffers b, ref SlotStatus st)
    {
        s.Legal.Clear();
        s.CandN = 0;
        if (s.Sim == null)
        {
            ObservationEncoder.EncodeEmpty(b);
            b.Cand.Clear();
            b.CandN = 0;
            return true;
        }
        try
        {
            ObservationEncoder.Encode(s.Sim, b, ref st);
            if (!s.Finished)
            {
                s.Sim.LegalActions(s.Raw);
                RewardMacros.Fill(s.Sim, s.Raw, s.Legal);
                if (Config.Macros != RoomMacroSet.None)
                {
                    RoomMacros.Apply(s.Sim, s.Legal, Config.Macros);
                }
            }
            s.CandN = CandidateTable.Fill(s.Sim, s.Legal, b.Cand, out bool overflow);
            if (overflow)
            {
                st |= SlotStatus.CandidateOverflow;
            }
            b.CandN = s.CandN;
            return true;
        }
        catch (Exception e)
        {
            st |= StatusClassifier.Classify(e);
            s.LastError = e.ToString();
            s.Legal.Clear();
            s.CandN = 0;
            ObservationEncoder.EncodeEmpty(b);
            b.Cand.Clear();
            b.CandN = 0;
            return false;
        }
    }

    /// <summary>The encoder's <c>is_combat</c>: a combat is in progress.</summary>
    private static bool InCombat(RunSim sim) => sim.State.Combat is { IsInProgress: true };

    /// <summary>
    /// Floor stall cap (<c>floor_step_cap</c> &gt; 0): the slot's current state is a non-combat decision point on the floor
    /// where it already took <c>floor_step_cap</c> non-combat decisions. This is the protocol evaluator's <c>stuck</c>
    /// rule moved to the step that produces the state it would refuse to decide on.
    /// </summary>
    private bool FloorStalled(VecSlot s)
        => Config.FloorStepCap > 0 && s.FloorSteps >= Config.FloorStepCap && s.Sim is { } sim && !InCombat(sim);

    private static void WriteLastEpisode(VecSlot s, SlotBuffers b, EndKind end, float reward)
    {
        var le = b.LastEpisode;
        le.Clear();
        le[LastEpCol.Win] = reward > 0f ? 1 : 0;
        le[LastEpCol.Steps] = s.StepIndex;
        le[LastEpCol.EndKind] = (int)end;
        try
        {
            if (s.Sim is { } sim)
                ObservationEncoder.FillEpisodeSummary(sim, le);
        }
        catch (Exception)
        {
            // A broken sim state still yields win/steps/end_kind.
        }
    }

    private static void WriteSignals(VecSlot s, SlotBuffers b, float reward, byte terminated, byte truncated, SlotStatus st)
    {
        s.Status = st;
        b.Reward = reward;
        b.Terminated = terminated;
        b.Truncated = truncated;
        b.Status = (int)st;
        var ep = b.Episode;
        ep[EpisodeCol.EpisodeId] = s.EpisodeCount > 0 ? s.EpisodeId : 0;
        ep[EpisodeCol.StepIndex] = s.StepIndex;
        ep[EpisodeCol.StartEntry] = s.StartEntry;
    }

    /// <summary>Last-resort handler for an exception outside the sim/encoder guards (an env bug): the slot is emptied.</summary>
    private static void Fault(VecSlot s, SlotBuffers b, Exception e, ref SlotStatus st)
    {
        st |= SlotStatus.SimError;
        s.LastError = e.ToString();
        s.Sim = null;
        s.Legal.Clear();
        s.CandN = 0;
        try
        {
            ObservationEncoder.EncodeEmpty(b);
            b.Cand.Clear();
            b.CandN = 0;
        }
        catch (Exception)
        {
            // buffers unusable; nothing more to do
        }
    }

    /// <summary>A branch destination starts a new episode: the counters of <see cref="StartSlot"/>, no pool entry (start_entry -1).</summary>
    private void BeginBranchEpisode(VecSlot s)
    {
        s.Sim = null;
        s.Finished = false;
        s.CandN = 0;
        s.Legal.Clear();
        s.Raw.Clear();
        s.StepIndex = 0;
        s.FloorSeen = -1;
        s.FloorSteps = 0;
        s.EpisodeId = s.EpisodeCount * NumEnvs + s.Index + 1;
        s.EpisodeCount++;
        s.StartEntry = -1;
    }

    /// <summary>Copies slot <paramref name="from"/>'s rows of every <see cref="BranchCopy"/> buffer to slot <paramref name="to"/>.</summary>
    private static void CopyBranchView(void** buffers, int from, int to)
    {
        foreach (int k in BranchCopy)
        {
            BufferView.Bytes(buffers, k, from).CopyTo(BufferView.Bytes(buffers, k, to));
        }
    }

    /// <summary>Runs body(0..count-1) on up to <c>threads</c> workers (inline when one thread or one item).</summary>
    private void ForEach(int count, Action<int> body)
    {
        if (_threads <= 1 || count <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }
            return;
        }
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = _threads }, body);
    }

    /// <summary>Every slot in range, listed once and not marked in <paramref name="sources"/> (null: none); throws naming the first problem.</summary>
    private void RequireDistinctSlots(ReadOnlySpan<int> slots, string what, bool[]? sources)
    {
        if (slots.Length > NumEnvs)
        {
            throw new ArgumentException($"{what} has {slots.Length} entries but the env has {NumEnvs} slots");
        }
        var seen = _seen;
        try
        {
            for (int j = 0; j < slots.Length; j++)
            {
                int s = slots[j];
                if ((uint)s >= (uint)NumEnvs)
                {
                    throw new ArgumentOutOfRangeException(what, $"{what}[{j}] = {s} outside 0..{NumEnvs - 1}");
                }
                if (sources != null && sources[s])
                {
                    throw new ArgumentException($"{what}[{j}] = {s} is a source slot of this call");
                }
                if (seen[s])
                {
                    throw new ArgumentException($"{what}[{j}] = {s} is listed twice");
                }
                seen[s] = true;
            }
        }
        finally
        {
            foreach (int s in slots)
            {
                if ((uint)s < (uint)NumEnvs)
                {
                    seen[s] = false;
                }
            }
        }
    }

    private void ForEachSlot(Action<int> body)
    {
        if (_threads <= 1 || NumEnvs == 1)
        {
            for (int i = 0; i < NumEnvs; i++)
            {
                body(i);
            }
            return;
        }
        Parallel.For(0, NumEnvs, new ParallelOptions { MaxDegreeOfParallelism = _threads }, body);
    }

    private void RequireSlot(int slot)
    {
        if ((uint)slot >= (uint)NumEnvs)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} outside 0..{NumEnvs - 1}");
        }
    }

    private static void RequireBuffers(void** buffers)
    {
        if (!BufferView.AllNonNull(buffers))
        {
            throw new ArgumentException($"buffers must be an array of {Buf.Count} non-null pointers in schema order");
        }
    }
}
