using Sts2Sim.Core.Run;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Diff.V2;

public sealed class StartFailure : Exception
{
    public StartFailure(string cls, string reason, bool isError = false)
        : base(reason)
    {
        Class = cls;
        IsError = isError;
    }

    public string Class { get; }

    public bool IsError { get; }
}

public static class AnchorLoader
{
    public const string FromSeed = "seed";
    public const string FromAnchor = "anchor";
    public const string ContentPortedOnly = "ported_only";
    public const string AnchorMcrInitial = "mcr_initial";

    public static string? StartFrom(BundleSegment segment) => segment.Start?["from"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public static RunSim SeedStart(RunBundle run, JsonObject start)
    {
        if (run.Seeding != RngSetup.SeedingXxh64)
        {
            throw new StartFailure(DivergenceClass.OutOfScope, $"a seed start needs xxh64 seeding (bundle: {run.Seeding})");
        }
        if (RngSetup.HasOldPrefix(run.Seed))
        {
            throw new StartFailure(DivergenceClass.OutOfScope, $"seed '{run.Seed}' carries the old prefix");
        }
        bool portedOnly = start["content"] is JsonValue cv && cv.TryGetValue(out string? content) && content == ContentPortedOnly;
        bool neow = start["neow"] is not JsonValue nv || !nv.TryGetValue(out bool n) || n;
        var unlocks = neow
            ? UnlockProfile.All
            : new UnlockProfile(UnlockProfile.AllEpochIds.Where(e => e != RunLoop.NeowEpochId), UnlockProfile.AllEncounterIds, UnlockProfile.AllNumberOfRuns);
        var config = new RunConfig(run.Seed, run.Character, run.Ascension)
        {
            Content = portedOnly ? ContentFilter.PortedOnly : ContentFilter.All,
            Unlocks = unlocks
        };
        if (start["act1"] is JsonValue av && av.TryGetValue(out string? act1) && act1 != null)
        {
            config = config with { Act1 = act1 };
        }
        try
        {
            return RunSim.NewRun(config);
        }
        catch (NotImplementedException ex)
        {
            throw new StartFailure(DivergenceClass.RunLayerNotPorted, "run_layer_not_ported:new_run: " + ex.Message);
        }
    }

    public static string ResolveRef(RunBundle run, string reference)
    {
        if (Path.IsPathRooted(reference))
        {
            return reference;
        }
        string dir = run.FilePath != null ? Path.GetDirectoryName(Path.GetFullPath(run.FilePath)) ?? "." : ".";
        return Path.GetFullPath(Path.Combine(dir, reference));
    }

    public static RunSim LoadSaveAnchor(RunBundle run, BundleAnchor anchor)
    {
        if (anchor.Ref == null)
        {
            throw new StartFailure(DivergenceClass.AnchorMissing, $"anchor_missing: anchor {anchor.Id} has no ref");
        }
        string path = ResolveRef(run, anchor.Ref);
        if (!File.Exists(path))
        {
            throw new StartFailure(DivergenceClass.AnchorMissing, $"anchor_missing: {anchor.Ref} not found");
        }
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StartFailure(DivergenceClass.AnchorError, $"anchor_unreadable: {ex.Message}");
        }
        if (anchor.Sha256 != null && !string.Equals(anchor.Sha256, RunBundleJson.Sha(text), StringComparison.OrdinalIgnoreCase))
        {
            throw new StartFailure(DivergenceClass.AnchorError, $"anchor_mismatch: sha256 of {anchor.Ref} differs from the bundle");
        }
        SavedRun save;
        try
        {
            save = RunSaveReader.Read(text);
        }
        catch (Exception ex) when (ex is not NotImplementedException)
        {
            throw new StartFailure(DivergenceClass.AnchorError, $"anchor_unreadable: {ex.GetType().Name}: {ex.Message}");
        }
        var reasons = RunImporter.CheckSupported(save, ImportOptions.Default);
        if (reasons.Count > 0)
        {
            string first = reasons[0];
            string cls = first.Contains("not ported", StringComparison.Ordinal) || first.Contains("is not registered", StringComparison.Ordinal)
                ? DivergenceClass.ContentNotPorted
                : DivergenceClass.OutOfScope;
            throw new StartFailure(cls, "anchor import: " + string.Join("; ", reasons));
        }
        ImportedRun imported;
        try
        {
            imported = RunImporter.ImportState(save, ImportOptions.Default);
        }
        catch (NotImplementedException ex)
        {
            throw new StartFailure(DivergenceClass.RunLayerNotPorted, "run_layer_not_ported:save_import: " + ex.Message);
        }
        var room = PreFinishedRoom(imported.PreFinishedRoom);
        try
        {
            return RunSim.FromLoadedState(imported.State, room);
        }
        catch (NotImplementedException ex)
        {
            throw new StartFailure(DivergenceClass.RunLayerNotPorted, "run_layer_not_ported:resume: " + ex.Message);
        }
    }

    public static AbstractRoom? PreFinishedRoom(SavedRoom? saved)
    {
        if (saved == null)
        {
            return null;
        }
        if (saved.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss && saved.EncounterId != null)
        {
            var room = new CombatRoom(EncounterRegistry.Get(saved.EncounterId), saved.RoomType)
            {
                GoldProportion = (decimal)saved.GoldProportion,
                ParentEventId = saved.ParentEventId,
                ShouldResumeParentEventAfterCombat = saved.ShouldResumeParentEvent
            };
            room.MarkPreFinished();
            return room;
        }
        throw new StartFailure(DivergenceClass.RunLayerNotPorted, $"run_layer_not_ported:resume: pre-finished {saved.RoomType} room");
    }
}
