namespace Sts2Sim.Vec;

/// <summary>Per-slot status bit flags written to the <c>status</c> buffer (CONTRACTS.md §D3.1 #30). Frozen.</summary>
[Flags]
public enum SlotStatus
{
    Ok = 0,
    /// <summary>The slot was not stepped (action &lt; 0).</summary>
    Noop = 1,
    /// <summary>The action index was outside [0, cand_n); the slot is unchanged.</summary>
    IllegalAction = 2,
    /// <summary>The sim threw NotImplementedException / NotSupportedException; the episode ended (truncated, reward 0).</summary>
    ContentGap = 4,
    /// <summary>The sim (or the encoder) threw any other exception; the episode ended (truncated, reward 0).</summary>
    SimError = 8,
    /// <summary>More legal actions than CAP_CANDIDATES; only the first CAP rows are listed and selectable.</summary>
    CandidateOverflow = 16,
    /// <summary>Some entity table exceeded its capacity; the first CAP rows were written.</summary>
    TableOverflow = 32,
    /// <summary>The slot was (re)started during this call (reset, restore or auto-reset).</summary>
    Reset = 64,
    /// <summary>A start was requested but the pool has no entries; the slot holds no run.</summary>
    PoolEmpty = 128,
    /// <summary>
    /// Appended (floor stall cap): the episode was truncated because it made <c>floor_step_cap</c> non-combat decisions
    /// on one floor without the floor advancing (truncated, reward 0; <c>last_episode.end_kind</c> = FloorStall).
    /// </summary>
    FloorStall = 256,
}

/// <summary>How an episode ended (<c>last_episode.end_kind</c>; enum list <c>EndKind</c> in the schema).</summary>
public enum EndKind
{
    None = 0,
    Terminal = 1,
    Truncated = 2,
    ContentGap = 3,
    SimError = 4,
    /// <summary>Appended: truncated by the floor stall cap (<see cref="SlotStatus.FloorStall"/>).</summary>
    FloorStall = 5,
}

public static class StatusClassifier
{
    /// <summary>D2: NotImplemented/NotSupported → CONTENT_GAP, everything else → SIM_ERROR.</summary>
    public static SlotStatus Classify(Exception e) => e is NotImplementedException or NotSupportedException
        ? SlotStatus.ContentGap
        : SlotStatus.SimError;

    public static EndKind EndKindOf(SlotStatus errorBit) => errorBit == SlotStatus.ContentGap ? EndKind.ContentGap : EndKind.SimError;
}
