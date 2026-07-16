using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

internal static class Hashing
{
    /// <summary>
    /// First 16 lowercase hex chars of the SHA-256 of the CANONICAL serialization
    /// of the state: object keys are sorted ordinally at every depth before
    /// serializing, so hash equality (and therefore snapshot dedup) is independent
    /// of the key insertion order used by the state builder. Array order is
    /// semantic and preserved.
    /// </summary>
    public static string StateHash(JsonNode state) => HashFromCanonical(CanonicalJson(state));

    /// <summary>
    /// The dedup hash of an already-computed canonical serialization (see
    /// <see cref="CanonicalJson"/>). Lets a caller canonicalize the state ONCE and
    /// reuse the string for both the hash and (indirectly) its own bookkeeping,
    /// instead of canonicalizing twice.
    /// </summary>
    public static string HashFromCanonical(string canonicalJson)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }

    /// <summary>Serialization with object keys recursively sorted (ordinal).</summary>
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
