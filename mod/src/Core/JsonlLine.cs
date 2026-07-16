using System;
using System.Buffers;
using System.Text.Json;

namespace Sts2Recorder.Core;

/// <summary>
/// Builds a single JSONL envelope line (record + trailing newline) as one UTF-8
/// byte buffer, ready to hand to <see cref="JsonlStreamWriter.Enqueue"/>.
///
/// Byte-exactness contract: the output must be byte-for-byte identical to the
/// former <c>new JsonObject{…}.ToJsonString() + "\n"</c> path (sts2rec and every
/// format test depend on it). This is achieved by writing the envelope with a
/// default <see cref="Utf8JsonWriter"/> — the SAME serializer STJ uses for
/// <c>JsonNode.ToJsonString()</c>, so number formatting (shortest round-trip
/// doubles, plain-decimal longs) and string escaping (<c>JavaScriptEncoder.Default</c>)
/// match exactly — and splicing the caller-provided, already-serialized child
/// JSON (state/params/data) verbatim via <see cref="Utf8JsonWriter.WriteRawValue"/>.
/// WriteRawValue copies the bytes without a parse+re-serialize round trip, which
/// is both the perf win (no re-serialization of the payload) and, because
/// <c>state.ToJsonString()</c> already produced canonical STJ output, byte-identical
/// to embedding the parsed node and letting the outer writer re-serialize it.
/// </summary>
internal static class JsonlLine
{
    // Default options == what JsonNode.ToJsonString(null) uses (no indent,
    // JavaScriptEncoder.Default). Keep this the ONLY writer configuration so the
    // byte-identity guarantee cannot silently drift.
    private static readonly JsonWriterOptions WriterOptions = default;

    /// <summary>state envelope: {seq,t,type:"state",trigger,screen,hash,state:&lt;raw&gt;}.</summary>
    public static byte[] State(
        long seq, double t, string trigger, string screen, string hash, string stateJson)
    {
        return Build(stateJson.Length + 128, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", seq);
            writer.WriteNumber("t", t);
            writer.WriteString("type", "state");
            writer.WriteString("trigger", trigger);
            writer.WriteString("screen", screen);
            writer.WriteString("hash", hash);
            writer.WritePropertyName("state");
            writer.WriteRawValue(stateJson, skipInputValidation: true);
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// action envelope:
    /// {seq,t,type:"action",source,action:{kind,params:&lt;raw&gt;},status,state_seq}.
    /// <paramref name="stateSeq"/> &gt; 0 is written as a number; otherwise JSON null
    /// (the "no prior snapshot" contract).
    /// </summary>
    public static byte[] Action(
        long seq, double t, string source, string kind, string paramsJson,
        string status, long stateSeq)
    {
        return Build(paramsJson.Length + 160, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", seq);
            writer.WriteNumber("t", t);
            writer.WriteString("type", "action");
            writer.WriteString("source", source);
            writer.WritePropertyName("action");
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            writer.WritePropertyName("params");
            writer.WriteRawValue(paramsJson, skipInputValidation: true);
            writer.WriteEndObject();
            writer.WriteString("status", status);
            if (stateSeq > 0)
            {
                writer.WriteNumber("state_seq", stateSeq);
            }
            else
            {
                writer.WriteNull("state_seq");
            }
            writer.WriteEndObject();
        });
    }

    /// <summary>event envelope: {seq,t,type:"event",entry,data:&lt;raw&gt;}.</summary>
    public static byte[] Event(long seq, double t, string entry, string dataJson)
    {
        return Build(dataJson.Length + 96, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", seq);
            writer.WriteNumber("t", t);
            writer.WriteString("type", "event");
            writer.WriteString("entry", entry);
            writer.WritePropertyName("data");
            writer.WriteRawValue(dataJson, skipInputValidation: true);
            writer.WriteEndObject();
        });
    }

    private static byte[] Build(int sizeHint, Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(Math.Max(sizeHint, 64));
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            write(writer);
        }
        var json = buffer.WrittenSpan;
        var line = new byte[json.Length + 1];
        json.CopyTo(line);
        line[json.Length] = (byte)'\n';
        return line;
    }
}
