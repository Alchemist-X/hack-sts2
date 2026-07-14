using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>Builds the manifest.json object (session format schema_version 1).</summary>
internal static class SessionMetaJson
{
    public const int SchemaVersion = 1;

    public static JsonObject BuildManifest(
        SessionMeta meta,
        long stateCount,
        long actionCount,
        long eventCount,
        bool incomplete,
        RunResult? result,
        int part,
        IReadOnlyCollection<string> degradedHooks)
    {
        var mods = new JsonArray();
        foreach (var mod in meta.Mods)
        {
            mods.Add((JsonNode)mod);
        }
        var hooks = new JsonArray();
        foreach (var hook in degradedHooks)
        {
            hooks.Add((JsonNode)hook);
        }
        return new JsonObject
        {
            ["schema_version"] = SchemaVersion,
            ["recorder_version"] = meta.RecorderVersion,
            ["game"] = new JsonObject
            {
                ["version"] = meta.GameVersion,
                ["commit"] = meta.GameCommit,
                ["build_id"] = meta.BuildId,
                ["untested"] = meta.Untested,
            },
            ["platform"] = meta.Platform,
            ["profile"] = meta.Profile,
            ["run"] = new JsonObject
            {
                ["seed"] = meta.Seed,
                ["character"] = meta.Character,
                ["ascension"] = meta.Ascension,
                ["game_mode"] = meta.GameMode,
                ["start_time"] = meta.StartTime,
            },
            ["result"] = BuildResult(result),
            ["counts"] = new JsonObject
            {
                ["states"] = stateCount,
                ["actions"] = actionCount,
                ["events"] = eventCount,
            },
            ["incomplete"] = incomplete,
            ["part"] = part,
            ["degraded_hooks"] = hooks,
            ["mods"] = mods,
        };
    }

    private static JsonNode? BuildResult(RunResult? result)
    {
        if (result is null)
        {
            return null;
        }
        return new JsonObject
        {
            ["win"] = result.Win,
            ["abandoned"] = result.Abandoned,
            ["end_time"] = result.EndTime,
        };
    }
}
