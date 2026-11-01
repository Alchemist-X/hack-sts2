using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>Run position at the moment a native snapshot is captured.</summary>
public sealed record AnchorContext(
    int? Act,
    int? Floor,
    string? RoomType,
    string? RoomModelId,
    string? MapPointType,
    int? MapCol,
    int? MapRow,
    long StateSeq)
{
    public static readonly AnchorContext Unknown = new(null, null, null, null, null, null, null, 0);
}

/// <summary>Profile fields that rules read but the run save does not carry.</summary>
public sealed record UnlockProfileSnapshot(
    IReadOnlyList<string> DiscoveredActs,
    int? NumberOfRuns,
    int? Wins,
    int? Losses,
    int? WongoPoints,
    int? TestSubjectKills,
    string? CharacterId,
    int? CharacterWins);

/// <summary>
/// Payload builders for the privileged native-anchor events (events.jsonl):
/// run_saved (R1), replay_archived (R2) and unlock_profile (R11). The session
/// adds file, sha256 and bytes when it archives the snapshot.
/// </summary>
public static class NativeAnchorEvents
{
    public const string SavesCategory = "saves";
    public const string ReplaysCategory = "replays";
    public const string SaveExtension = ".save";
    public const string ReplayExtension = ".mcr";

    public const string RunSavedEntry = "run_saved";
    public const string ReplayArchivedEntry = "replay_archived";
    public const string UnlockProfileEntry = "unlock_profile";

    public const string TriggerSaved = "saved";
    public const string TriggerRunStarted = "run_started";
    public const string TriggerWriteReplay = "write_replay";

    public const string KindRoomEntry = "room_entry";
    public const string KindCombatExit = "combat_exit";
    public const string KindAncientExit = "ancient_exit";
    public const string KindPreFinished = "pre_finished";

    /// <summary>room_entry when the save has no pre_finished_room, else by its room type.</summary>
    public static string AnchorKind(RunSaveInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!info.HasPreFinishedRoom)
        {
            return KindRoomEntry;
        }
        switch (info.PreFinishedRoomType?.ToLowerInvariant())
        {
            case "monster":
            case "elite":
            case "boss":
                return KindCombatExit;
            case "event":
                return KindAncientExit;
            default:
                return KindPreFinished;
        }
    }

    public static JsonObject RunSaved(string trigger, RunSaveInfo info, AnchorContext context)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(context);
        var data = new JsonObject
        {
            ["trigger"] = trigger,
            ["anchor_kind"] = AnchorKind(info),
        };
        AddContext(data, context);
        data["pre_finished"] = info.HasPreFinishedRoom;
        data["pre_finished_room_type"] = info.PreFinishedRoomType;
        data["schema_version"] = info.SchemaVersion;
        data["start_time"] = info.StartTime;
        data["save_act_index"] = info.CurrentActIndex;
        data["num_reloads"] = info.NumReloads;
        if (info.Error != null)
        {
            data["parse_error"] = info.Error;
        }
        return data;
    }

    public static JsonObject ReplayArchived(bool stopRecording, bool duringCleanUp, AnchorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = new JsonObject
        {
            ["trigger"] = TriggerWriteReplay,
            ["stop_recording"] = stopRecording,
            ["during_cleanup"] = duringCleanUp,
        };
        AddContext(data, context);
        return data;
    }

    public static JsonObject UnlockProfile(UnlockProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var acts = new List<string>(snapshot.DiscoveredActs);
        acts.Sort(StringComparer.Ordinal);
        var actArray = new JsonArray();
        foreach (var act in acts)
        {
            actArray.Add((JsonNode)act);
        }
        return new JsonObject
        {
            ["source"] = "SaveManager.Progress",
            ["discovered_acts"] = actArray,
            ["number_of_runs"] = snapshot.NumberOfRuns,
            ["wins"] = snapshot.Wins,
            ["losses"] = snapshot.Losses,
            ["wongo_points"] = snapshot.WongoPoints,
            ["test_subject_kills"] = snapshot.TestSubjectKills,
            ["character_id"] = snapshot.CharacterId,
            ["character_wins"] = snapshot.CharacterWins,
        };
    }

    private static void AddContext(JsonObject data, AnchorContext context)
    {
        data["state_seq"] = context.StateSeq > 0 ? context.StateSeq : null;
        data["act"] = context.Act;
        data["floor"] = context.Floor;
        data["room_type"] = context.RoomType;
        data["room_model_id"] = context.RoomModelId;
        data["map_point_type"] = context.MapPointType;
        data["map_coord"] = context.MapCol.HasValue && context.MapRow.HasValue
            ? new JsonObject { ["col"] = context.MapCol.Value, ["row"] = context.MapRow.Value }
            : null;
    }
}

/// <summary>Localization-key helpers for event pages.</summary>
public static class LocKeys
{
    private const string PagesSegment = ".pages.";

    /// <summary>"NEOW.pages.INITIAL.description" -> "INITIAL"; null when the key has no page segment.</summary>
    public static string? PageKey(string? locEntryKey)
    {
        if (string.IsNullOrEmpty(locEntryKey))
        {
            return null;
        }
        var index = locEntryKey.IndexOf(PagesSegment, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }
        var start = index + PagesSegment.Length;
        if (start >= locEntryKey.Length)
        {
            return null;
        }
        var end = locEntryKey.IndexOf('.', start);
        var page = end < 0 ? locEntryKey[start..] : locEntryKey[start..end];
        return page.Length > 0 ? page : null;
    }
}

/// <summary>Where an archived native snapshot went and whether its event was recorded.</summary>
public sealed record NativeArchiveReceipt(long Seq, string RelativePath, string Sha256, int Bytes, bool EventRecorded);
