using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sts2Recorder.Core;

/// <summary>
/// One recording session = one run. Owns the three append-only JSONL streams
/// (states/actions/events) plus manifest.json, per docs/design.md session format
/// schema_version 1. Thread-safe: a single lock serializes all writes, and the
/// record seq is session-global and strictly monotonic across all three streams.
///
/// Flushing: the recurring save-heartbeat flush (queue drain + fsync + manifest
/// rewrite) runs on a dedicated background flusher thread via
/// <see cref="RequestFlush"/>, so the game thread never blocks on disk latency.
/// Lock order everywhere: _flushLock before _lock (never the reverse); _flushSignal
/// is a leaf lock and is never held while taking another. Native snapshot copies
/// (<see cref="ArchiveNativeSnapshot"/>) keep their own writer thread and drain.
/// </summary>
public sealed class TrajectorySession : IDisposable
{
    private static readonly IReadOnlyList<string> AllowedActionStatuses =
        new[] { "committed", "executed", "cancelled" };

    private static readonly TimeSpan NativeDrainTimeout = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly object _flushLock = new();
    private readonly object _flushSignal = new();
    private readonly IClock _clock;
    private readonly SessionMeta _meta;
    private readonly int _part;
    private readonly JsonlStreamWriter _states;
    private readonly JsonlStreamWriter _actions;
    private readonly JsonlStreamWriter _events;
    private readonly ErrorLog _errorLog;
    private readonly NativeFileWriter _native;
    private readonly List<string> _degradedHooks = new();
    private readonly Thread _flushThread;
    private bool _flushRequested;
    private bool _flushShutdown;

    // Last cut successfully fsynced + published by FlushCore. Guarded by
    // _flushLock (both the flusher thread and sync Flush hold it).
    private long _flushedStateLines = -1;
    private long _flushedActionLines = -1;
    private long _flushedEventLines = -1;

    private long _nextSeq = 1;
    private long _latestStateSeq;
    private string? _lastStateHash;
    private RunResult? _result;
    private bool _completed;
    private bool _disposed;
    private string? _compression;
    private long _droppedAfterComplete;
    private long _snapshotBuildCount;
    private double _snapshotBuildTotalMs;
    private double _snapshotBuildMaxMs;
    // Distribution of the same per-snapshot cost that feeds count/avg/max
    // (bounded buckets: p95/p99 upper bounds), plus the split by trigger.
    private readonly LatencyHistogram _snapshotBuildLatency = new();
    private readonly Dictionary<string, LatencyHistogram> _snapshotBuildByTrigger = new();
    private long _statesDeduplicated;

    private TrajectorySession(string directory, int part, SessionMeta meta, IClock clock)
    {
        Directory = directory;
        _part = part;
        _meta = meta;
        _clock = clock;
        _errorLog = new ErrorLog(directory, clock);
        _native = new NativeFileWriter(OnStreamError);
        // Dispose-on-throw: a failing constructor must not leak already-opened
        // JSONL FileStream handles into game code.
        JsonlStreamWriter? states = null;
        JsonlStreamWriter? actions = null;
        JsonlStreamWriter? events = null;
        try
        {
            states = new JsonlStreamWriter(Path.Combine(directory, "states.jsonl"), OnStreamError);
            actions = new JsonlStreamWriter(Path.Combine(directory, "actions.jsonl"), OnStreamError);
            events = new JsonlStreamWriter(Path.Combine(directory, "events.jsonl"), OnStreamError);
            _states = states;
            _actions = actions;
            _events = events;
            WriteManifestLocked();
            _flushThread = new Thread(FlusherLoop)
            {
                IsBackground = true,
                Name = "sts2rec-session-flusher",
            };
            _flushThread.Start();
        }
        catch
        {
            states?.Dispose();
            actions?.Dispose();
            events?.Dispose();
            _native.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates &lt;outputRoot&gt;/sessions/&lt;StartTime&gt;-&lt;Seed&gt;/ (with a -partN
    /// suffix when the run id already exists, i.e. a resumed run across a game
    /// restart) and writes an initial incomplete manifest.
    /// </summary>
    public static TrajectorySession Begin(string outputRoot, SessionMeta meta, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(clock);
        var (directory, part) = SessionPaths.CreateSessionDirectory(outputRoot, meta);
        return new TrajectorySession(directory, part, meta, clock);
    }

    /// <summary>Absolute path of this session's directory.</summary>
    public string Directory { get; }

    /// <summary>Seq of the most recent state snapshot; 0 if none has been recorded yet.</summary>
    public long LatestStateSeq
    {
        get
        {
            lock (_lock)
            {
                return _latestStateSeq;
            }
        }
    }

    /// <summary>True after Complete(); late lifecycle handlers use this to skip work whose records would be dropped anyway.</summary>
    public bool IsCompleted
    {
        get
        {
            lock (_lock)
            {
                return _completed;
            }
        }
    }

    /// <summary>
    /// Records a state snapshot. Hash-dedup: when the serialized state is identical
    /// to the previous snapshot, nothing is written and the previous seq is returned.
    /// <paramref name="buildMillis"/> (optional) is how long the state builder took;
    /// it feeds the manifest "perf" counters and is accumulated even when the
    /// snapshot dedupes (the build cost was paid either way).
    /// </summary>
    public long RecordState(string trigger, string screen, JsonNode state, double? buildMillis = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(state);
        // Dedup hash first, via the streaming canonical form (no cloned tree, no
        // intermediate string). The payload serialization is deferred until the
        // dedup check has passed, so a discarded duplicate pays only the hash —
        // ~30% of builds dedup in practice and previously paid a full
        // serialization each.
        var hash = Hashing.HashState(state);
        lock (_lock)
        {
            ThrowIfUnusable();
            if (buildMillis is { } ms)
            {
                AddSnapshotCostLocked(ms, trigger);
            }
            if (_completed)
            {
                NoteDroppedAfterCompleteLocked("RecordState");
                return _latestStateSeq;
            }
            if (hash == _lastStateHash)
            {
                _statesDeduplicated++;
                return _latestStateSeq;
            }
            var seq = _nextSeq++;
            // Kept state is written straight into the UTF-8 envelope buffer
            // (builder's key order; canonical order is a hash-only concept) —
            // no intermediate UTF-16 string. The write+flush syscall runs
            // off-thread. LatestStateSeq is updated synchronously below, so a
            // subsequent RecordAction sees this seq regardless of whether the
            // bytes have physically been written yet.
            _states.Enqueue(JsonlLine.State(seq, _clock.Now, trigger, screen, hash, state));
            _latestStateSeq = seq;
            _lastStateHash = hash;
            return seq;
        }
    }

    /// <summary>
    /// Records a hooked action. <paramref name="status"/> must be "committed"
    /// (already-final decisions), "executed" or "cancelled" (GameAction lifecycle).
    /// <paramref name="stateSeq"/> null means "the latest snapshot" (LatestStateSeq).
    /// When no snapshot exists yet (LatestStateSeq == 0, or a caller passes a
    /// stale 0), state_seq is written as JSON null — the contract for "action
    /// fired before the first snapshot"; sts2rec canonical degrades gracefully
    /// (next snapshot as best-effort state_before, flagged in info).
    /// </summary>
    public long RecordAction(
        string source, string kind, JsonNode parameters, string status, long? stateSeq)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(kind);
        if (!Contains(AllowedActionStatuses, status))
        {
            throw new ArgumentException(
                $"unknown action status '{status}' (expected committed|executed|cancelled)",
                nameof(status));
        }
        var serializedParams = parameters?.ToJsonString() ?? "{}";
        lock (_lock)
        {
            ThrowIfUnusable();
            if (_completed)
            {
                NoteDroppedAfterCompleteLocked("RecordAction");
                return 0;
            }
            var seq = _nextSeq++;
            var resolvedStateSeq = stateSeq ?? _latestStateSeq;
            _actions.Enqueue(JsonlLine.Action(
                seq, _clock.Now, source, kind, serializedParams, status, resolvedStateSeq));
            return seq;
        }
    }

    /// <summary>Records a fine-grained history entry on the privileged events stream.</summary>
    public long RecordEvent(string entry, JsonNode data)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var serializedData = data?.ToJsonString() ?? "{}";
        lock (_lock)
        {
            ThrowIfUnusable();
            if (_completed)
            {
                NoteDroppedAfterCompleteLocked("RecordEvent");
                return 0;
            }
            var seq = _nextSeq++;
            _events.Enqueue(JsonlLine.Event(seq, _clock.Now, entry, serializedData));
            return seq;
        }
    }

    /// <summary>
    /// Adds main-thread snapshot cost (state build + hash + envelope
    /// serialization, measured by the caller around the whole snapshot) to the
    /// manifest "perf.snapshot_build_ms" counters — the alternative to passing
    /// <c>buildMillis</c> to <see cref="RecordState"/>, for callers that want
    /// the hash/serialize cost included. <paramref name="trigger"/> (optional)
    /// also files the cost under "perf.capture_by_trigger_ms". Never throws
    /// after Dispose (perf bookkeeping must not break the snapshot path).
    /// </summary>
    public void RecordSnapshotCost(double millis, string? trigger = null)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            AddSnapshotCostLocked(millis, trigger);
        }
    }

    private void AddSnapshotCostLocked(double millis, string? trigger)
    {
        _snapshotBuildCount++;
        _snapshotBuildTotalMs += millis;
        _snapshotBuildMaxMs = Math.Max(_snapshotBuildMaxMs, millis);
        _snapshotBuildLatency.Add(millis);
        if (trigger is null)
        {
            return;
        }
        // Bounded key set: unexpected trigger labels fold into "other".
        var key = _snapshotBuildByTrigger.ContainsKey(trigger) || _snapshotBuildByTrigger.Count < 32
            ? trigger
            : "other";
        if (!_snapshotBuildByTrigger.TryGetValue(key, out var latency))
        {
            _snapshotBuildByTrigger[key] = latency = new LatencyHistogram();
        }
        latency.Add(millis);
    }

    /// <summary>
    /// Marks a hook as degraded (disabled after a game update); deduplicated.
    /// Deliberately synchronous (manifest rewritten before returning): hooks
    /// degrade at patch/subscribe time — startup and run-start, never mid-combat —
    /// so this is off the hot path, and the degraded marker must survive an
    /// immediate crash.
    /// </summary>
    public void AddDegradedHook(string hookId)
    {
        ArgumentNullException.ThrowIfNull(hookId);
        lock (_flushLock)
        {
            lock (_lock)
            {
                ThrowIfUnusable();
                if (_degradedHooks.Contains(hookId))
                {
                    return;
                }
                _degradedHooks.Add(hookId);
                WriteManifestLocked();
            }
        }
    }

    /// <summary>Copies (never moves) a native game artifact into &lt;session&gt;/native/.</summary>
    public void ArchiveNative(string sourcePath, string destName)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destName);
        lock (_lock)
        {
            ThrowIfUnusable();
            var nativeDir = Path.Combine(Directory, SessionPaths.NativeDirName);
            System.IO.Directory.CreateDirectory(nativeDir);
            File.Copy(sourcePath, Path.Combine(nativeDir, destName), overwrite: true);
        }
    }

    /// <summary>
    /// Archives a native snapshot the caller already read (the bytes are copied
    /// on entry; the game's own file is never opened here). Assigns the next seq,
    /// records <paramref name="entry"/> on the events stream with
    /// <paramref name="data"/> plus file, sha256 and bytes, and queues an
    /// off-thread write to native/&lt;category&gt;/&lt;seq:D6&gt;&lt;extension&gt;
    /// that never overwrites. After Complete the file is still written but no
    /// event is recorded (EventRecorded=false); its seq is above every record seq.
    /// </summary>
    public NativeArchiveReceipt ArchiveNativeSnapshot(
        string category, string extension, string entry, byte[] bytes, JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(data);
        if (!IsSafeSegment(category))
        {
            throw new ArgumentException($"invalid native category '{category}'", nameof(category));
        }
        if (extension.Length < 2 || extension[0] != '.' || !IsSafeSegment(extension[1..]))
        {
            throw new ArgumentException($"invalid native extension '{extension}'", nameof(extension));
        }
        var owned = (byte[])bytes.Clone();
        var sha256 = Convert.ToHexString(SHA256.HashData(owned)).ToLowerInvariant();
        lock (_lock)
        {
            ThrowIfUnusable();
            var seq = _nextSeq++;
            var fileName = seq.ToString("D6", CultureInfo.InvariantCulture) + extension;
            var relative = SessionPaths.NativeDirName + "/" + category + "/" + fileName;
            var absolute = Path.Combine(Directory, SessionPaths.NativeDirName, category, fileName);
            var recorded = !_completed;
            if (recorded)
            {
                var payload = new JsonObject
                {
                    ["file"] = relative,
                    ["sha256"] = sha256,
                    ["bytes"] = owned.Length,
                };
                foreach (var pair in data)
                {
                    if (!payload.ContainsKey(pair.Key))
                    {
                        payload[pair.Key] = pair.Value?.DeepClone();
                    }
                }
                _events.Enqueue(JsonlLine.Event(seq, _clock.Now, entry, payload.ToJsonString()));
            }
            _native.Enqueue(absolute, owned);
            return new NativeArchiveReceipt(seq, relative, sha256, owned.Length, recorded);
        }
    }

    /// <summary>Blocks until every queued native snapshot write has finished; false on timeout.</summary>
    public bool WaitForNativeWrites(TimeSpan timeout)
    {
        lock (_lock)
        {
            ThrowIfUnusable();
        }
        return _native.WaitIdle(timeout);
    }

    /// <summary>
    /// Rate-limited error report; appended to recorder.log in the session dir.
    /// After Dispose this is a silent no-op (the error path must never throw
    /// into game code, so it does not use the throwing disposed gate).
    /// </summary>
    public void RecordError(string where, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(ex);
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
        }
        _errorLog.Record(where, ex);
    }

    /// <summary>
    /// Synchronously drains + fsyncs all streams and atomically rewrites the
    /// manifest (still incomplete). Graceful-teardown path; the recurring save
    /// heartbeat uses <see cref="RequestFlush"/> so the game thread never blocks
    /// here. After Complete/Dispose the manifest part is a no-op (the terminal
    /// manifest is authoritative).
    /// </summary>
    public void Flush()
    {
        lock (_flushLock)
        {
            lock (_lock)
            {
                ThrowIfUnusable();
            }
            FlushCore();
        }
    }

    /// <summary>
    /// Queues an asynchronous flush (drain + fsync + manifest rewrite) on the
    /// background flusher thread and returns immediately — the save-heartbeat
    /// entry point. Requests coalesce: any number of calls while a flush is in
    /// flight collapse into one trailing flush. Never throws; after
    /// Complete/Dispose it is a silent no-op.
    /// </summary>
    public void RequestFlush()
    {
        lock (_flushSignal)
        {
            if (_flushShutdown)
            {
                return;
            }
            _flushRequested = true;
            Monitor.Pulse(_flushSignal);
        }
    }

    private void FlusherLoop()
    {
        while (true)
        {
            lock (_flushSignal)
            {
                while (!_flushRequested && !_flushShutdown)
                {
                    Monitor.Wait(_flushSignal);
                }
                if (_flushShutdown)
                {
                    return;
                }
                _flushRequested = false;
            }
            try
            {
                lock (_flushLock)
                {
                    FlushCore();
                }
            }
            catch (Exception ex)
            {
                // The flusher must neither die silently nor throw; surface disk
                // trouble through the session error channel and keep serving.
                _errorLog.Record("TrajectorySession.FlusherLoop", ex);
            }
        }
    }

    /// <summary>
    /// Drain + fsync + manifest rewrite. Must hold <c>_flushLock</c>; must NOT
    /// hold <c>_lock</c> across the I/O. Counts are captured as ONE consistent
    /// cross-stream cut under the session lock (records enqueue under that same
    /// lock, so an action inside the cut always has its referenced state inside
    /// the cut too), the streams are drained to exactly those counts —
    /// referents (states) before dependents (actions/events) — and the manifest
    /// claims exactly them. Lines enqueued while the drain runs are not
    /// claimed, so the manifest may briefly undercount the file but never
    /// overcount it (the crash contract). On a disk fault the previous
    /// checkpoint is kept (the fault is already in recorder.log). Perf counters
    /// may lag one flush behind when only deduped builds occurred; the terminal
    /// manifest refreshes them. Native snapshot files are NOT waited for here
    /// (their own writer thread drains them; Complete/Dispose wait for it).
    /// </summary>
    private void FlushCore()
    {
        long stateLines, stateBytes, actionLines, actionBytes, eventLines, eventBytes;
        lock (_lock)
        {
            if (_disposed || _completed)
            {
                return;
            }
            (stateLines, stateBytes) = _states.Counts();
            (actionLines, actionBytes) = _actions.Counts();
            (eventLines, eventBytes) = _events.Counts();
        }
        if (stateLines == _flushedStateLines
            && actionLines == _flushedActionLines
            && eventLines == _flushedEventLines)
        {
            // Idle heartbeat: this cut is already durable and published — skip
            // the 4 fsyncs + manifest rewrite entirely.
            return;
        }
        if (!_states.FlushUpTo(stateLines)
            || !_actions.FlushUpTo(actionLines)
            || !_events.FlushUpTo(eventLines))
        {
            return;
        }
        JsonObject manifest;
        lock (_lock)
        {
            if (_disposed || _completed)
            {
                // The terminal manifest (Complete/Dispose) is authoritative; a
                // late flush must not resurrect an incomplete one over it.
                return;
            }
            manifest = BuildManifestLocked(
                stateLines, actionLines, eventLines,
                stateBytes, actionBytes, eventBytes);
        }
        ManifestWriter.WriteAtomic(Directory, manifest);
        _flushedStateLines = stateLines;
        _flushedActionLines = actionLines;
        _flushedEventLines = eventLines;
    }

    /// <summary>
    /// Signals the flusher to exit and joins it. Idempotent; called outside
    /// <c>_flushLock</c> so an in-flight flush can finish and observe shutdown.
    /// </summary>
    private void StopFlusher()
    {
        lock (_flushSignal)
        {
            _flushShutdown = true;
            Monitor.PulseAll(_flushSignal);
        }
        if (!ReferenceEquals(Thread.CurrentThread, _flushThread))
        {
            _flushThread.Join();
        }
    }

    /// <summary>
    /// Finalizes the session: result set, incomplete=false, counts final. The
    /// three JSONL streams are closed and gzip-compressed (.jsonl.gz) with the
    /// plain files deleted only after a byte-verified round-trip decompression;
    /// on any compression failure the plain files are kept, the error goes to
    /// recorder.log, and the manifest records compression=null. Records arriving
    /// after Complete are dropped (counted, one recorder.log notice) — the
    /// finalized counts/compressed bytes must stay authoritative.
    /// </summary>
    public void Complete(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            // _flushLock first (lock order): a flusher-thread flush in flight
            // finishes before the terminal manifest is written and can never
            // overwrite it afterwards (FlushCore rechecks _completed).
            lock (_flushLock)
            {
                lock (_lock)
                {
                    ThrowIfUnusable();
                    if (_completed)
                    {
                        throw new InvalidOperationException("session already completed");
                    }
                    _result = result;
                    _completed = true;
                    _native.WaitIdle(NativeDrainTimeout);
                    // Freeze the streams (final flush + close) so the compressor sees
                    // stable bytes; LineCount/BytesWritten remain readable afterwards.
                    _states.Dispose();
                    _actions.Dispose();
                    _events.Dispose();
                    if (SessionCompressor.TryCompressStreams(
                            Directory, (where, ex) => _errorLog.Record(where, ex)))
                    {
                        _compression = SessionCompressor.Format;
                    }
                    WriteManifestLocked();
                }
            }
        }
        finally
        {
            // Even a disk-full IOException from the terminal manifest write must
            // not leak the flusher thread.
            StopFlusher();
        }
    }

    /// <summary>
    /// Flushes and closes; without a prior Complete() the manifest stays
    /// incomplete:true. Teardown order: an in-flight async flush finishes first
    /// (_flushLock), then a final SYNCHRONOUS drain + fsync + manifest rewrite
    /// covers every record enqueued so far, then the JSONL writers and the
    /// native writer (which drains its queued copies) are disposed, and the
    /// flusher thread is stopped.
    /// </summary>
    public void Dispose()
    {
        try
        {
            lock (_flushLock)
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    _disposed = true;
                    try
                    {
                        // After Complete the terminal manifest is already on disk
                        // and authoritative — do not rewrite it here, or a fault
                        // mid-rewrite could replace a finalized manifest.
                        if (!_completed)
                        {
                            WriteManifestLocked();
                        }
                    }
                    finally
                    {
                        _states.Dispose();
                        _actions.Dispose();
                        _events.Dispose();
                        _native.Dispose();
                    }
                }
            }
        }
        finally
        {
            StopFlusher();
        }
    }

    /// <summary>
    /// Bridges a background writer-thread fault into the session error channel
    /// (recorder.log). Invoked from the writer thread; ErrorLog is thread-safe and
    /// uses an independent lock, and the writer already fell back to synchronous
    /// writes so no data is dropped — the game thread is never touched.
    /// </summary>
    private void OnStreamError(string where, Exception ex) => _errorLog.Record(where, ex);

    private void FlushStreamsLocked()
    {
        // Null-conditional: WriteManifestLocked runs once from the constructor,
        // where a partially-constructed instance could have unassigned writers.
        _states?.Flush();
        _actions?.Flush();
        _events?.Flush();
    }

    /// <summary>
    /// Terminal/constructor manifest write with LIVE counts. Streams must be
    /// flushed BEFORE the rewrite: the manifest counts reflect in-memory
    /// LineCount, so buffered-but-unflushed lines would make a crash-terminated
    /// session's manifest claim lines that never reached disk. Heartbeat flushes
    /// go through <see cref="FlushCore"/> with captured counts instead.
    /// </summary>
    private void WriteManifestLocked()
    {
        FlushStreamsLocked();
        var (stateLines, stateBytes) = _states?.Counts() ?? (0L, 0L);
        var (actionLines, actionBytes) = _actions?.Counts() ?? (0L, 0L);
        var (eventLines, eventBytes) = _events?.Counts() ?? (0L, 0L);
        var manifest = BuildManifestLocked(
            stateLines, actionLines, eventLines,
            stateBytes, actionBytes, eventBytes);
        ManifestWriter.WriteAtomic(Directory, manifest);
    }

    private JsonObject BuildManifestLocked(
        long stateCount, long actionCount, long eventCount,
        long statesBytes, long actionsBytes, long eventsBytes)
    {
        var manifest = SessionMetaJson.BuildManifest(
            _meta,
            stateCount: stateCount,
            actionCount: actionCount,
            eventCount: eventCount,
            incomplete: !_completed,
            result: _result,
            part: _part,
            degradedHooks: _degradedHooks,
            compression: _compression,
            perf: new SessionPerf(
                SnapshotBuildCount: _snapshotBuildCount,
                SnapshotBuildTotalMs: _snapshotBuildTotalMs,
                SnapshotBuildMaxMs: _snapshotBuildMaxMs,
                StatesBytes: statesBytes,
                ActionsBytes: actionsBytes,
                EventsBytes: eventsBytes));
        AddPerfDetailLocked((JsonObject)manifest["perf"]!);
        return manifest;
    }

    /// <summary>
    /// Optional manifest "perf" detail on top of the schema-1 counters:
    /// p95/p99 upper bounds for snapshot_build_ms (count/avg/max unchanged),
    /// the same cost split by snapshot trigger, the hash-dedup count, and the
    /// writer queue depths (current + peak).
    /// </summary>
    private void AddPerfDetailLocked(JsonObject perf)
    {
        var build = (JsonObject)perf["snapshot_build_ms"]!;
        var distribution = _snapshotBuildLatency.Describe();
        build["p95_upper_bound"] = distribution["p95_upper_bound"]!.DeepClone();
        build["p99_upper_bound"] = distribution["p99_upper_bound"]!.DeepClone();
        var byTrigger = new JsonObject();
        foreach (var pair in _snapshotBuildByTrigger)
        {
            byTrigger[pair.Key] = pair.Value.Describe();
        }
        perf["capture_by_trigger_ms"] = byTrigger;
        perf["states_deduplicated"] = _statesDeduplicated;
        perf["writer_queues"] = new JsonObject
        {
            // Null-conditional: the constructor's first manifest write can run
            // before every writer is assigned (see FlushStreamsLocked).
            ["states"] = _states?.QueueStats(),
            ["actions"] = _actions?.QueueStats(),
            ["events"] = _events?.QueueStats(),
        };
    }

    /// <summary>
    /// Post-Complete records are dropped by design (streams are closed and
    /// compressed); the first drop leaves a rate-limited recorder.log notice
    /// so late-firing game handlers are visible without breaking finalized
    /// counts.
    /// </summary>
    private void NoteDroppedAfterCompleteLocked(string where)
    {
        _droppedAfterComplete++;
        if (_droppedAfterComplete == 1)
        {
            _errorLog.Record(
                $"TrajectorySession.{where}",
                new InvalidOperationException(
                    "record arrived after Complete(); dropped (session finalized)"));
        }
    }

    private void ThrowIfUnusable()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TrajectorySession));
        }
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Contains(IReadOnlyList<string> values, string candidate)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
