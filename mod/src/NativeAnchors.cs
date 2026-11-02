using System;
using System.Collections.Generic;
using System.IO;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// Passive native anchors (docs/hook-map.md, v0.111.0 addendum): copies of the
/// game's own current_run.save (R1) and replays/latest.mcr (R2) into the
/// session's native/ tree, and the unlock-profile fields the save lacks (R11).
/// Everything here reads files the game has already finished writing and
/// records to the privileged events stream; nothing touches game state.
/// </summary>
internal static class NativeAnchors
{
    private const string RunSaveRelativePath = "saves/" + RunSaveManager.runSaveFileName;

    /// <summary>
    /// R1: reads the profile-scoped current_run.save right after the game wrote
    /// it and archives a copy. <paramref name="requireStartTime"/> skips a save
    /// that belongs to another run (used for the run-start copy on resume).
    /// </summary>
    internal static NativeArchiveReceipt? CaptureRunSave(
        TrajectorySession session, RunState? runState, string trigger, long? requireStartTime)
    {
        var path = RecorderMod.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(RunSaveRelativePath));
        if (!File.Exists(path))
        {
            return null;
        }
        var bytes = File.ReadAllBytes(path);
        var info = RunSaveInfo.Read(bytes);
        if (requireStartTime.HasValue && info.StartTime != requireStartTime.Value)
        {
            return null;
        }
        var data = NativeAnchorEvents.RunSaved(trigger, info, Context(session, runState));
        return session.ArchiveNativeSnapshot(
            NativeAnchorEvents.SavesCategory, NativeAnchorEvents.SaveExtension,
            NativeAnchorEvents.RunSavedEntry, bytes, data);
    }

    /// <summary>R2: archives the replay file CombatReplayWriter.WriteReplay just wrote.</summary>
    internal static NativeArchiveReceipt? CaptureReplay(
        TrajectorySession session, RunState? runState, string globalizedPath, bool stopRecording, bool duringCleanUp)
    {
        if (!File.Exists(globalizedPath))
        {
            return null;
        }
        var bytes = File.ReadAllBytes(globalizedPath);
        var data = NativeAnchorEvents.ReplayArchived(stopRecording, duringCleanUp, Context(session, runState));
        return session.ArchiveNativeSnapshot(
            NativeAnchorEvents.ReplaysCategory, NativeAnchorEvents.ReplayExtension,
            NativeAnchorEvents.ReplayArchivedEntry, bytes, data);
    }

    /// <summary>R11: profile progress fields read by rules but absent from the run save.</summary>
    internal static void RecordUnlockProfile(TrajectorySession session, RunState state)
    {
        var progress = SaveManager.Instance.Progress;
        var acts = new List<string>();
        try
        {
            foreach (var act in progress.DiscoveredActs)
            {
                acts.Add(act.ToString());
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("NativeAnchors.RecordUnlockProfile.DiscoveredActs", ex);
        }
        var character = JsonDescribe.Try<ModelId>(() => LocalContext.GetMe(state)?.Character.Id);
        var snapshot = new UnlockProfileSnapshot(
            DiscoveredActs: acts,
            NumberOfRuns: JsonDescribe.Try<int?>(() => progress.NumberOfRuns),
            Wins: JsonDescribe.Try<int?>(() => progress.Wins),
            Losses: JsonDescribe.Try<int?>(() => progress.Losses),
            WongoPoints: JsonDescribe.Try<int?>(() => progress.WongoPoints),
            TestSubjectKills: JsonDescribe.Try<int?>(() => progress.TestSubjectKills),
            CharacterId: JsonDescribe.Try(() => character?.ToString()),
            CharacterWins: JsonDescribe.Try<int?>(
                () => character != null ? progress.GetStatsForCharacter(character)?.TotalWins : null));
        session.RecordEvent(NativeAnchorEvents.UnlockProfileEntry, NativeAnchorEvents.UnlockProfile(snapshot));
    }

    private static AnchorContext Context(TrajectorySession session, RunState? runState)
    {
        var stateSeq = JsonDescribe.Try<long?>(() => session.LatestStateSeq) ?? 0;
        if (runState == null)
        {
            return AnchorContext.Unknown with { StateSeq = stateSeq };
        }
        var coord = JsonDescribe.Try<MapCoord?>(() => runState.CurrentMapCoord);
        return new AnchorContext(
            Act: JsonDescribe.Try<int?>(() => runState.CurrentActIndex),
            Floor: JsonDescribe.Try<int?>(() => runState.TotalFloor),
            RoomType: JsonDescribe.Try(() => runState.CurrentRoom?.RoomType.ToString()),
            RoomModelId: JsonDescribe.Try(() => runState.CurrentRoom?.ModelId?.ToString()),
            MapPointType: JsonDescribe.Try(() => runState.CurrentMapPoint?.PointType.ToString()),
            MapCol: coord?.col,
            MapRow: coord?.row,
            StateSeq: stateSeq);
    }
}
