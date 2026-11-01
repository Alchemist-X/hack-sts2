using System;
using System.Text.Json;

namespace Sts2Recorder.Core;

/// <summary>
/// The few top-level fields of a copied current_run.save that the run_saved
/// event carries. Read with a forward-only reader; nested values are skipped.
/// A malformed file yields Error and whatever was read before the fault.
/// </summary>
public sealed record RunSaveInfo(
    int? SchemaVersion,
    long? StartTime,
    int? CurrentActIndex,
    int? NumReloads,
    bool HasPreFinishedRoom,
    string? PreFinishedRoomType,
    string? Error)
{
    private static ReadOnlySpan<byte> Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF };

    public static RunSaveInfo Read(ReadOnlySpan<byte> utf8Json)
    {
        int? schemaVersion = null;
        long? startTime = null;
        int? actIndex = null;
        int? numReloads = null;
        var preFinished = false;
        string? preFinishedRoomType = null;
        try
        {
            if (utf8Json.StartsWith(Utf8Bom))
            {
                utf8Json = utf8Json[Utf8Bom.Length..];
            }
            var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return new RunSaveInfo(null, null, null, null, false, null, "top level is not a JSON object");
            }
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                {
                    break;
                }
                switch (name)
                {
                    case "schema_version":
                        schemaVersion = ReadInt(ref reader);
                        break;
                    case "start_time":
                        startTime = ReadLong(ref reader);
                        break;
                    case "current_act_index":
                        actIndex = ReadInt(ref reader);
                        break;
                    case "num_reloads":
                        numReloads = ReadInt(ref reader);
                        break;
                    case "pre_finished_room":
                        if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            preFinished = true;
                            preFinishedRoomType = ReadRoomType(ref reader);
                        }
                        else
                        {
                            reader.Skip();
                        }
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return new RunSaveInfo(
                schemaVersion, startTime, actIndex, numReloads, preFinished, preFinishedRoomType, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new RunSaveInfo(
                schemaVersion, startTime, actIndex, numReloads, preFinished, preFinishedRoomType,
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static int? ReadInt(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
        {
            return value;
        }
        reader.Skip();
        return null;
    }

    private static long? ReadLong(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var value))
        {
            return value;
        }
        reader.Skip();
        return null;
    }

    private static string? ReadRoomType(ref Utf8JsonReader reader)
    {
        string? roomType = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            if (!reader.Read())
            {
                break;
            }
            if (name == "room_type" && reader.TokenType == JsonTokenType.String)
            {
                roomType = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }
        return roomType;
    }
}
