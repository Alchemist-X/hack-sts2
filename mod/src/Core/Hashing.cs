using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

internal static class Hashing
{
    /// <summary>
    /// First 16 lowercase hex chars of the SHA-256 of the CANONICAL serialization
    /// of the state: object keys are sorted ordinally at every depth before
    /// serializing, so hash equality (and therefore snapshot dedup) is independent
    /// of the key insertion order used by the state builder. Array order is
    /// semantic and preserved. Streams the canonical form straight into a UTF-8
    /// buffer — no cloned tree, no intermediate string, no byte[] copy. This runs
    /// on the Godot main thread per snapshot build, so the allocation savings
    /// matter; byte-equivalence with the legacy tree-clone composition is pinned
    /// by HashingTests.
    /// </summary>
    public static string HashState(JsonNode? state)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 8 * 1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, state);
        }
        var digest = SHA256.HashData(buffer.WrittenSpan);
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                writer.WriteStartObject();
                // Plain list + in-place sort instead of a LINQ OrderBy pipeline:
                // this runs for every object node of every snapshot hash on the
                // main thread, and the sorter/enumerator allocations add up.
                var entries = new List<KeyValuePair<string, JsonNode?>>(obj.Count);
                foreach (var pair in obj)
                {
                    entries.Add(pair);
                }
                entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var pair in entries)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteCanonical(writer, pair.Value);
                }
                writer.WriteEndObject();
                break;
            }
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// Legacy tree-clone canonicalization path. Kept ONLY as the independent
    /// test oracle that pins <see cref="HashState"/>'s byte-equivalence
    /// (HashingTests / AsyncWriterRegressionTests) — an accidental format change
    /// in the streaming writer fails the pinning tests against this
    /// implementation. Not on any production path; do not call from the hot path.
    /// </summary>
    public static string HashFromCanonical(string canonicalJson)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }

    /// <summary>Serialization with object keys recursively sorted (ordinal). Test oracle; see <see cref="HashFromCanonical"/>.</summary>
    internal static string CanonicalJson(JsonNode? node) =>
        Canonicalize(node)?.ToJsonString() ?? "null";

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(
            obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, JsonNode?>(
                    pair.Key, Canonicalize(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonicalize).ToArray()),
        JsonValue value => value.DeepClone(),
        _ => null,
    };
}
