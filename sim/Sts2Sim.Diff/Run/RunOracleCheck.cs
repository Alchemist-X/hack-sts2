namespace Sts2Sim.Diff.Run;

public enum RunOracleStatus
{
    Agree,
    Diverge,
    Unsupported,
    Error
}

public sealed record RunOracleCheck(string Oracle, string Source, RunOracleStatus Status, string Detail, int Compared = 0)
{
    public static RunOracleCheck Agree(string oracle, string source, int compared, string detail = "")
        => new(oracle, source, RunOracleStatus.Agree, detail, compared);

    public static RunOracleCheck Diverge(string oracle, string source, int compared, string detail)
        => new(oracle, source, RunOracleStatus.Diverge, detail, compared);

    public static RunOracleCheck Unsupported(string oracle, string source, string reason)
        => new(oracle, source, RunOracleStatus.Unsupported, reason);

    public static RunOracleCheck Error(string oracle, string source, Exception ex)
        => new(oracle, source, RunOracleStatus.Error, $"{ex.GetType().Name}: {ex.Message}");

    public static RunOracleCheck Guard(string oracle, string source, Func<RunOracleCheck> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return body();
        }
        catch (NotImplementedException ex)
        {
            return Unsupported(oracle, source, "sim stub: " + ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return Unsupported(oracle, source, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(oracle, source, ex);
        }
    }
}

public static class RunOracleNames
{
    public const string RunStart = "run_start";
    public const string Neow = "neow";
    public const string MapSaved = "map_saved";
    public const string MapGenerated = "map_generated";
    public const string MapPath = "map_path";
    public const string UnknownRolls = "unknown_rolls";
    public const string EncounterPulls = "encounter_pulls";
    public const string EventPulls = "event_pulls";

    public static readonly IReadOnlyList<string> All = new[] { RunStart, Neow, MapSaved, MapGenerated, MapPath, UnknownRolls, EncounterPulls, EventPulls };
}
