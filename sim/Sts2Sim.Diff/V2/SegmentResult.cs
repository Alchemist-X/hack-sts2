namespace Sts2Sim.Diff.V2;

public static class DivergenceClass
{
    public const string PortBug = "port_bug";
    public const string ProjectionGap = "projection_gap";
    public const string ExporterBug = "exporter_bug";
    public const string HarnessBug = "harness_bug";
    public const string RecorderGap = "recorder_gap";
    public const string RecorderBug = "recorder_bug";
    public const string AnchorError = "anchor_error";
    public const string VersionDrift = "version_drift";
    public const string DecompileGap = "decompile_gap";
    public const string Nondeterminism = "nondeterminism";
    public const string OutOfScope = "out_of_scope";
    public const string ContentNotPorted = "content_not_ported";
    public const string RunLayerNotPorted = "run_layer_not_ported";
    public const string AnchorMissing = "anchor_missing";
    public const string ExporterIneligible = "exporter_ineligible";
    public const string None = "none";
}

public static class V2DivergenceKind
{
    public const string State = DivergenceKind.State;
    public const string Action = DivergenceKind.Action;
    public const string Terminal = DivergenceKind.Terminal;
    public const string Final = DivergenceKind.Final;
    public const string RoomEntry = "room_entry";
    public const string FloorSummary = "floor_summary";
    public const string Screen = "screen";
}

public sealed class SegmentDivergence
{
    public required string Kind { get; init; }

    public int DecisionIndex { get; init; }

    public required FieldDiff First { get; init; }

    public IReadOnlyList<FieldDiff> AllHard { get; init; } = Array.Empty<FieldDiff>();

    public JsonNode? Action { get; init; }

    public JsonNode? OracleProjection { get; init; }

    public JsonNode? SimProjection { get; init; }
}

public sealed class SegmentResult
{
    public string? Path { get; init; }

    public required string Lineage { get; init; }

    public required string SegmentId { get; init; }

    public required string SegmentKind { get; init; }

    public string GameVersion { get; init; } = BundleRunner.UnknownVersion;

    public bool FromV1 { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string StartMode { get; set; } = "none";

    public bool ReplayedIneligible { get; set; }

    public BundleStatus Status { get; set; }

    public string? Reason { get; set; }

    public string Class { get; set; } = DivergenceClass.None;

    public string? ExceptionDetail { get; set; }

    public int? ExceptionDecisionIndex { get; set; }

    public int DecisionsTotal { get; set; }

    public int DecisionsCompared { get; set; }

    public int DecisionsAgreed { get; set; }

    public int DecisionsUnobserved { get; set; }

    // W4.1: mcr play_card decisions whose hand index the sim re-resolved (V2Runner.ResolveMcrPlayIndex).
    public int McrPlayIndicesResolved { get; set; }

    public List<int> AgreedIndices { get; } = new();

    public SegmentDivergence? Divergence { get; set; }

    public List<SoftDiff> SoftDiffs { get; } = new();

    public bool OutcomeChecked { get; set; }

    public bool OutcomeAgreed { get; set; }

    public string? SimOutcome { get; set; }

    public int? SimFinalHp { get; set; }

    public bool FloorSummaryChecked { get; set; }

    public bool FloorSummaryAgreed { get; set; }

    public List<JsonNode?> ActionsPrefix { get; } = new();

    public Dictionary<int, HashSet<ContentKey>> Touched { get; } = new();

    public HashSet<ContentKey> SegmentTouched { get; } = new();

    public string Key => Lineage + "/" + SegmentId;
}

public readonly record struct ContentKey(string Area, string Id)
{
    public override string ToString() => Area + ":" + Id;
}
