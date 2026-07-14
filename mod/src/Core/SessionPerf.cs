namespace Sts2Recorder.Core;

/// <summary>
/// Per-session performance counters, written into the manifest "perf" object
/// at every Flush/Complete. SnapshotBuild* counts every state-builder run
/// reported through RecordState (including builds whose snapshot deduped);
/// Bytes* are uncompressed bytes appended to each JSONL stream.
/// </summary>
public sealed record SessionPerf(
    long SnapshotBuildCount,
    double SnapshotBuildTotalMs,
    double SnapshotBuildMaxMs,
    long StatesBytes,
    long ActionsBytes,
    long EventsBytes)
{
    public static readonly SessionPerf Empty = new(0, 0, 0, 0, 0, 0);
}
