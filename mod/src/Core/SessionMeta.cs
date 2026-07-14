using System.Collections.Generic;

namespace Sts2Recorder.Core;

/// <summary>Immutable run metadata stamped into every manifest.</summary>
public sealed record SessionMeta(
    string RecorderVersion,
    string GameVersion,
    string GameCommit,
    string BuildId,
    string Platform,
    string Profile,
    string Seed,
    string Character,
    int Ascension,
    string GameMode,
    long StartTime,
    bool Untested,
    IReadOnlyList<string> Mods);

/// <summary>Final outcome of a run, written to the manifest by Complete().</summary>
public sealed record RunResult(bool Win, bool Abandoned, double EndTime);
