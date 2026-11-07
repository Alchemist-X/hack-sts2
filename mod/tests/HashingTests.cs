using System.Text.Json.Nodes;
using Xunit;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// The streaming state hash (<see cref="Hashing.HashState"/>) must produce the
/// exact hash of the legacy canonicalize-then-serialize composition
/// (<see cref="Hashing.HashFromCanonical"/> over <see cref="Hashing.CanonicalJson"/>)
/// for any state: offline tooling and cross-part dedup compare these hashes, so
/// the streaming fast path may not change a single byte of the canonical form.
/// </summary>
public sealed class HashingTests
{
    public static TheoryData<string, JsonNode?> Corpus()
    {
        return new TheoryData<string, JsonNode?>
        {
            { "empty_object", new JsonObject() },
            { "empty_array", new JsonArray() },
            { "null_root", null },
            { "scalar_string", JsonValue.Create("hello") },
            { "scalar_number", JsonValue.Create(42) },
            {
                "unsorted_keys",
                new JsonObject
                {
                    ["zeta"] = 1,
                    ["alpha"] = 2,
                    ["Mixed"] = 3,
                    ["_underscore"] = 4,
                }
            },
            {
                "nested_mixed",
                new JsonObject
                {
                    ["b"] = new JsonObject
                    {
                        ["y"] = new JsonArray(1, 2, new JsonObject { ["k"] = "v" }),
                        ["x"] = JsonValue.Create(true),
                    },
                    ["a"] = new JsonArray("s", null, false, 1.5),
                }
            },
            {
                "unicode_and_escapes",
                new JsonObject
                {
                    ["中文键"] = "值 with \"quotes\" and \\backslash\\ and \nnewline",
                    ["emoji"] = "🎴🃏",
                    ["control"] = "tab\tchar",
                }
            },
            {
                "numbers",
                new JsonObject
                {
                    ["int"] = 0,
                    ["neg"] = -17,
                    ["big"] = 9007199254740993L,
                    ["frac"] = 0.125,
                    ["sci"] = 1e10,
                    ["neg_frac"] = -0.5,
                }
            },
            {
                "array_of_objects_unsorted",
                new JsonArray(
                    new JsonObject { ["z"] = 1, ["a"] = 2 },
                    new JsonObject { ["a"] = 2, ["z"] = 1 })
            },
        };
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void HashState_MatchesLegacyCanonicalComposition(string label, JsonNode? state)
    {
        Assert.NotNull(label);
        var legacy = Hashing.HashFromCanonical(Hashing.CanonicalJson(state));
        var streaming = Hashing.HashState(state);
        Assert.Equal(legacy, streaming);
    }

    [Fact]
    public void HashState_IsKeyOrderIndependent()
    {
        var a = new JsonObject
        {
            ["first"] = 1,
            ["second"] = new JsonObject { ["x"] = "v", ["a"] = "w" },
        };
        var b = new JsonObject
        {
            ["second"] = new JsonObject { ["a"] = "w", ["x"] = "v" },
            ["first"] = 1,
        };
        Assert.Equal(Hashing.HashState(a), Hashing.HashState(b));
    }

    [Fact]
    public void HashState_IsArrayOrderSensitive()
    {
        var a = new JsonArray(1, 2, 3);
        var b = new JsonArray(3, 2, 1);
        Assert.NotEqual(Hashing.HashState(a), Hashing.HashState(b));
    }

    [Fact]
    public void HashState_DiffersForDifferentContent()
    {
        var a = new JsonObject { ["hp"] = 10 };
        var b = new JsonObject { ["hp"] = 11 };
        Assert.NotEqual(Hashing.HashState(a), Hashing.HashState(b));
    }
}
