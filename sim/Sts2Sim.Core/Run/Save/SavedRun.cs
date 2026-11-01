namespace Sts2Sim.Core.Run;

public enum SavePoint : byte
{
    EnteringMapPoint,
    PostCombatRewards,
    AncientFinished,
    NoVisitedCoord
}

public sealed class SavedRun
{
    public const int SupportedSchemaVersion = 20;

    public int SchemaVersion { get; init; }

    public string Json { get; init; } = string.Empty;
}

public sealed record ImportOptions
{
    public static readonly ImportOptions Default = new();

    public RunProfileProgress Progress { get; init; } = RunProfileProgress.Veteran;

    public ContentFilter Content { get; init; } = ContentFilter.All;
}

public sealed record RunImportResult(RunSim? Sim, SavePoint? EntryPoint, IReadOnlyList<string> UnsupportedReasons)
{
    public bool IsSupported => Sim != null && UnsupportedReasons.Count == 0;
}
