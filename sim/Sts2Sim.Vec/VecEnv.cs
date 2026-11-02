using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sts2Sim.Core;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>config.pool.fresh (CONTRACTS.md §D4): <c>Count</c> entries, character = Characters[i % n], seed "&lt;SeedPrefix&gt;-&lt;character&gt;-&lt;i&gt;".</summary>
public sealed record FreshPoolSpec(IReadOnlyList<string> Characters, int Ascension = 0, int Count = 4096, string SeedPrefix = "TRAIN", string Act1 = RunConfig.RandomAct1);

/// <summary>Env configuration (§D5 config_json). JSON keys are snake_case; unknown keys are rejected.</summary>
public sealed record VecConfig(
    int NumEnvs,
    int Threads = 8,
    int StepCap = 3000,
    bool AutoReset = true,
    string SeedText = "VEC",
    string Content = "ported_only",
    FreshPoolSpec? Pool = null,
    int PoolCache = 4096)
{
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
        _ = ContentFilter;
        if (Pool != null)
        {
            if (Pool.Characters.Count == 0) throw new ArgumentException("pool.fresh.characters is empty");
            if (Pool.Count < 0) throw new ArgumentException("pool.fresh.count must be >= 0");
        }
    }

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

    /// <summary>Number of selectable candidates published in the last observation.</summary>
    public int CandN { get; internal set; }

    /// <summary>LegalActions() at the last observation; candidate i = Legal[i].</summary>
    internal List<RunAction> Legal { get; } = new(64);
}

/// <summary>
/// Batched environment (CONTRACTS.md §D2; owner of the game-logic bodies: V2). All calls are batch calls over the slots;
/// buffers are caller-owned (<see cref="Schema.Buffers"/> order). Per-slot problems go to <c>status</c>; methods throw only
/// on invalid arguments (the exports turn that into -1 + last_error).
/// </summary>
public sealed unsafe class VecEnv
{
    private readonly int[] _actions;
    private readonly byte[] _mask;
    private readonly int[] _starts;
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
    /// </summary>
    public void Reset(ReadOnlySpan<byte> mask, ReadOnlySpan<int> startIds, void** buffers)
    {
        RequireBuffers(buffers);
        if (mask.Length != 0 && mask.Length != NumEnvs) throw new ArgumentException($"mask has {mask.Length} entries, expected {NumEnvs}");
        if (startIds.Length != 0 && startIds.Length != NumEnvs) throw new ArgumentException($"start_ids has {startIds.Length} entries, expected {NumEnvs}");
        int count = Pool.Count;
        for (int i = 0; i < NumEnvs; i++)
        {
            _mask[i] = mask.Length == 0 ? (byte)1 : mask[i];
            _starts[i] = startIds.Length == 0 ? -1 : startIds[i];
            if (_mask[i] != 0 && _starts[i] >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIds), $"start_ids[{i}] = {_starts[i]} but the pool has {count} entries");
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

    /// <summary>Resets one slot from one pool entry (E4 search) and writes that slot's observation.</summary>
    public void Restore(int slot, int entry, void** buffers)
    {
        RequireBuffers(buffers);
        RequireSlot(slot);
        if ((uint)entry >= (uint)Pool.Count) throw new ArgumentOutOfRangeException(nameof(entry), $"pool entry {entry} does not exist (count {Pool.Count})");
        ResetSlot(slot, entry, buffers);
    }

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
            else if (s.Sim == null || s.Finished || action >= s.CandN)
            {
                st |= SlotStatus.IllegalAction;
            }
            else
            {
                try
                {
                    var act = s.Legal[action];
                    if (FaultInjector?.Invoke(i, act) is { } injected)
                    {
                        throw injected;
                    }
                    s.Sim.Step(act);
                    s.StepIndex++;
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

    /// <summary>Starts a new episode on the slot from <paramref name="entry"/> or (entry &lt; 0) the slot's start sequence.</summary>
    private void StartSlot(VecSlot s, int entry, ref SlotStatus st)
    {
        s.Sim = null;
        s.Finished = false;
        s.CandN = 0;
        s.Legal.Clear();
        s.StepIndex = 0;
        st |= SlotStatus.Reset;
        int count = Pool.Count;
        if (entry < 0)
        {
            if (count == 0)
            {
                st |= SlotStatus.PoolEmpty;
                s.StartEntry = -1;
                return;
            }
            entry = (int)((s.SlotSeed + (ulong)s.EpisodeCount) % (ulong)count);
        }
        s.EpisodeId = s.EpisodeCount * NumEnvs + s.Index + 1;
        s.EpisodeCount++;
        s.StartEntry = entry;
        try
        {
            s.Sim = Pool.Materialize(entry);
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
                s.Sim.LegalActions(s.Legal);
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
