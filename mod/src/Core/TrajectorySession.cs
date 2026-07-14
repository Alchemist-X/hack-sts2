using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>
/// One recording session = one run. Owns the three append-only JSONL streams
/// (states/actions/events) plus manifest.json, per docs/design.md session format
/// schema_version 1. Thread-safe: a single lock serializes all writes, and the
/// record seq is session-global and strictly monotonic across all three streams.
/// </summary>
public sealed class TrajectorySession : IDisposable
{
    private static readonly IReadOnlyList<string> AllowedActionStatuses =
        new[] { "committed", "executed", "cancelled" };

    private readonly object _lock = new();
    private readonly IClock _clock;
    private readonly SessionMeta _meta;
    private readonly int _part;
    private readonly JsonlStreamWriter _states;
    private readonly JsonlStreamWriter _actions;
    private readonly JsonlStreamWriter _events;
    private readonly ErrorLog _errorLog;
    private readonly List<string> _degradedHooks = new();

    private long _nextSeq = 1;
    private long _latestStateSeq;
    private string? _lastStateHash;
    private RunResult? _result;
    private bool _completed;
    private bool _disposed;

    private TrajectorySession(string directory, int part, SessionMeta meta, IClock clock)
    {
        Directory = directory;
        _part = part;
        _meta = meta;
        _clock = clock;
        _errorLog = new ErrorLog(directory, clock);
        _states = new JsonlStreamWriter(Path.Combine(directory, "states.jsonl"));
        _actions = new JsonlStreamWriter(Path.Combine(directory, "actions.jsonl"));
        _events = new JsonlStreamWriter(Path.Combine(directory, "events.jsonl"));
        WriteManifestLocked();
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

    /// <summary>
    /// Records a state snapshot. Hash-dedup: when the serialized state is identical
    /// to the previous snapshot, nothing is written and the previous seq is returned.
    /// </summary>
    public long RecordState(string trigger, string screen, JsonNode state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var serialized = state.ToJsonString();
        var hash = Hashing.StateHash(serialized);
        lock (_lock)
        {
            ThrowIfUnusable();
            if (hash == _lastStateHash)
            {
                return _latestStateSeq;
            }
            var seq = _nextSeq++;
            _states.WriteLine(new JsonObject
            {
                ["seq"] = seq,
                ["t"] = _clock.Now,
                ["type"] = "state",
                ["trigger"] = trigger,
                ["screen"] = screen,
                ["hash"] = hash,
                ["state"] = JsonNode.Parse(serialized),
            });
            _latestStateSeq = seq;
            _lastStateHash = hash;
            return seq;
        }
    }

    /// <summary>
    /// Records a hooked action. <paramref name="status"/> must be "committed"
    /// (already-final decisions), "executed" or "cancelled" (GameAction lifecycle).
    /// <paramref name="stateSeq"/> null means "the latest snapshot" (LatestStateSeq).
    /// </summary>
    public long RecordAction(
        string source, string kind, JsonNode parameters, string status, long? stateSeq)
    {
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
            var seq = _nextSeq++;
            _actions.WriteLine(new JsonObject
            {
                ["seq"] = seq,
                ["t"] = _clock.Now,
                ["type"] = "action",
                ["source"] = source,
                ["action"] = new JsonObject
                {
                    ["kind"] = kind,
                    ["params"] = JsonNode.Parse(serializedParams),
                },
                ["status"] = status,
                ["state_seq"] = stateSeq ?? _latestStateSeq,
            });
            return seq;
        }
    }

    /// <summary>Records a fine-grained history entry on the privileged events stream.</summary>
    public long RecordEvent(string entry, JsonNode data)
    {
        var serializedData = data?.ToJsonString() ?? "{}";
        lock (_lock)
        {
            ThrowIfUnusable();
            var seq = _nextSeq++;
            _events.WriteLine(new JsonObject
            {
                ["seq"] = seq,
                ["t"] = _clock.Now,
                ["type"] = "event",
                ["entry"] = entry,
                ["data"] = JsonNode.Parse(serializedData),
            });
            return seq;
        }
    }

    /// <summary>Marks a hook as degraded (disabled after a game update); deduplicated.</summary>
    public void AddDegradedHook(string hookId)
    {
        ArgumentNullException.ThrowIfNull(hookId);
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

    /// <summary>Rate-limited error report; appended to recorder.log in the session dir.</summary>
    public void RecordError(string where, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(ex);
        _errorLog.Record(where, ex);
    }

    /// <summary>Flushes all streams and atomically rewrites the manifest (still incomplete).</summary>
    public void Flush()
    {
        lock (_lock)
        {
            ThrowIfUnusable();
            FlushStreamsLocked();
            WriteManifestLocked();
        }
    }

    /// <summary>Finalizes the session: result set, incomplete=false, counts final.</summary>
    public void Complete(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_lock)
        {
            ThrowIfUnusable();
            if (_completed)
            {
                throw new InvalidOperationException("session already completed");
            }
            _result = result;
            _completed = true;
            FlushStreamsLocked();
            WriteManifestLocked();
        }
    }

    /// <summary>Flushes and closes; without a prior Complete() the manifest stays incomplete:true.</summary>
    public void Dispose()
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
                WriteManifestLocked();
            }
            finally
            {
                _states.Dispose();
                _actions.Dispose();
                _events.Dispose();
            }
        }
    }

    private void FlushStreamsLocked()
    {
        _states.Flush();
        _actions.Flush();
        _events.Flush();
    }

    private void WriteManifestLocked()
    {
        var manifest = SessionMetaJson.BuildManifest(
            _meta,
            stateCount: _states?.LineCount ?? 0,
            actionCount: _actions?.LineCount ?? 0,
            eventCount: _events?.LineCount ?? 0,
            incomplete: !_completed,
            result: _result,
            part: _part,
            degradedHooks: _degradedHooks);
        ManifestWriter.WriteAtomic(Directory, manifest);
    }

    private void ThrowIfUnusable()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TrajectorySession));
        }
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
