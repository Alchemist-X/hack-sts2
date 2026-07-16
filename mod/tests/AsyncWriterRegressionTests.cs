using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Regression coverage for the async ordered-writer optimization: the envelope
/// bytes must stay byte-for-byte identical to the former
/// <c>JsonObject{…}.ToJsonString() + "\n"</c> path, and the async write must not
/// weaken any ordering / linkage / drain invariant.
/// </summary>
public sealed class AsyncWriterRegressionTests
{
    // --- (a) byte-for-byte identity vs the former JsonObject serialization -----

    private static byte[] LegacyState(
        long seq, double t, string trigger, string screen, string hash, string serialized) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["seq"] = seq,
            ["t"] = t,
            ["type"] = "state",
            ["trigger"] = trigger,
            ["screen"] = screen,
            ["hash"] = hash,
            ["state"] = JsonNode.Parse(serialized),
        }.ToJsonString() + "\n");

    private static byte[] LegacyAction(
        long seq, double t, string source, string kind, string serializedParams,
        string status, long resolvedStateSeq) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["seq"] = seq,
            ["t"] = t,
            ["type"] = "action",
            ["source"] = source,
            ["action"] = new JsonObject
            {
                ["kind"] = kind,
                ["params"] = JsonNode.Parse(serializedParams),
            },
            ["status"] = status,
            ["state_seq"] = resolvedStateSeq > 0 ? (JsonNode)resolvedStateSeq : null,
        }.ToJsonString() + "\n");

    private static byte[] LegacyEvent(long seq, double t, string entry, string serializedData) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["seq"] = seq,
            ["t"] = t,
            ["type"] = "event",
            ["entry"] = entry,
            ["data"] = JsonNode.Parse(serializedData),
        }.ToJsonString() + "\n");

    [Fact]
    public void StateLineIsByteIdenticalToLegacyEnvelope()
    {
        // Deliberately exercise the fragile bits: a whole-number double (must
        // serialize WITHOUT a decimal point), a fractional double, strings STJ
        // escapes (<, >, &, ", emoji), and a nested state with arrays/numbers.
        var state = new JsonObject
        {
            ["screen"] = "combat <fx> & \"boss\"",
            ["floor"] = 12,
            ["hp"] = 41.5,
            ["hand"] = new JsonArray("CARD.ZAP", "CARD.STRIKE", "★"),
            ["nested"] = new JsonObject { ["x"] = 1, ["emoji"] = "🔥" },
        };
        var serialized = state.ToJsonString();
        var hash = Hashing.HashFromCanonical(Hashing.CanonicalJson(state));

        foreach (var t in new[] { 1773034386.0, 1773034386.25, 0.1 })
        {
            Assert.Equal(
                LegacyState(7, t, "phase", "combat", hash, serialized),
                JsonlLine.State(7, t, "phase", "combat", hash, serialized));
        }
    }

    [Fact]
    public void ActionLineIsByteIdenticalToLegacyEnvelope()
    {
        var parameters = new JsonObject
        {
            ["card"] = "CARD.ZAP & <x>",
            ["target"] = 0,
            ["nested"] = new JsonObject { ["a"] = 1 },
        };
        var serializedParams = parameters.ToJsonString();

        // state_seq present (> 0)
        Assert.Equal(
            LegacyAction(9, 1000.5, "hook:PlayCardPatch", "play_card", serializedParams, "executed", 3),
            JsonlLine.Action(9, 1000.5, "hook:PlayCardPatch", "play_card", serializedParams, "executed", 3));
        // state_seq null (0 → JSON null, the "no prior snapshot" contract)
        Assert.Equal(
            LegacyAction(2, 1000.0, "hook:MapPatch", "map_choice", "{}", "committed", 0),
            JsonlLine.Action(2, 1000.0, "hook:MapPatch", "map_choice", "{}", "committed", 0));
    }

    [Fact]
    public void EventLineIsByteIdenticalToLegacyEnvelope()
    {
        var data = new JsonObject { ["card"] = "CARD.ZAP", ["amount"] = 8, ["u"] = "→" };
        var serializedData = data.ToJsonString();
        Assert.Equal(
            LegacyEvent(4, 1234.75, "card_drawn", serializedData),
            JsonlLine.Event(4, 1234.75, "card_drawn", serializedData));
    }

    [Fact]
    public void SessionStateLineMatchesLegacyBytesEndToEnd()
    {
        // The bytes actually written by a live session for a state must equal the
        // legacy envelope for the same (seq, t, trigger, screen, hash, state).
        var root = NewTempRoot();
        var state = new JsonObject { ["screen"] = "combat", ["floor"] = 1, ["energy"] = 3 };
        var serialized = state.ToJsonString();
        var hash = Hashing.HashFromCanonical(Hashing.CanonicalJson(state));

        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime, step: 0));
        var seq = session.RecordState("phase", "combat", state);
        session.Flush();

        var onDisk = File.ReadAllBytes(Path.Combine(session.Directory, "states.jsonl"));
        // FakeClock with step 0 keeps Now == StartTime for the single read.
        Assert.Equal(LegacyState(seq, StartTime, "phase", "combat", hash, serialized), onDisk);
    }

    // --- (c) dedup skips consecutive identical states and allocates no seq ------

    [Fact]
    public void ConsecutiveIdenticalStateDedupesAndConsumesNoSeq()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s1 = session.RecordState("phase", "combat",
            new JsonObject { ["floor"] = 1, ["energy"] = 3 });
        var dup = session.RecordState("poll", "combat",
            new JsonObject { ["floor"] = 1, ["energy"] = 3 });
        var next = session.RecordState("action", "combat",
            new JsonObject { ["floor"] = 1, ["energy"] = 2 });
        session.Flush();

        Assert.Equal(s1, dup);          // dedup returns prior seq
        Assert.Equal(s1 + 1, next);     // no seq was consumed by the dedup
        Assert.Equal(2, ReadJsonl(session.Directory, "states").Count);
    }

    // --- (d) after Flush(), on-disk counts == manifest counts -------------------

    [Fact]
    public void AfterFlushOnDiskCountsEqualManifestCounts()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime, 0.001));
        for (var i = 0; i < 60; i++)
        {
            session.RecordState("poll", "combat", new JsonObject { ["i"] = i });
            session.RecordAction("hook:X", "play_card", new JsonObject { ["i"] = i }, "executed", null);
            session.RecordEvent("card_drawn", new JsonObject { ["i"] = i });
        }
        session.Flush();

        var manifest = ReadManifest(session.Directory).GetProperty("counts");
        Assert.Equal(
            ReadJsonl(session.Directory, "states").Count,
            manifest.GetProperty("states").GetInt64());
        Assert.Equal(
            ReadJsonl(session.Directory, "actions").Count,
            manifest.GetProperty("actions").GetInt64());
        Assert.Equal(
            ReadJsonl(session.Directory, "events").Count,
            manifest.GetProperty("events").GetInt64());
    }

    // --- (e) Dispose drains all queued lines to disk ----------------------------

    [Fact]
    public void DisposeDrainsAllQueuedSessionLinesToDisk()
    {
        var root = NewTempRoot();
        string dir;
        const int n = 150;
        using (var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime, 0.001)))
        {
            dir = session.Directory;
            for (var i = 0; i < n; i++)
            {
                session.RecordEvent("card_drawn", new JsonObject { ["i"] = i });
            }
            // No explicit Flush: Dispose must drain + join before the file closes.
        }
        Assert.Equal(n, ReadJsonl(dir, "events").Count);
    }

    // --- (f) action.state_seq == seq of the immediately-preceding kept state ----

    [Fact]
    public void ActionStateSeqTracksLatestKeptStateEvenWhenWritesAreAsync()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime));
        var s1 = session.RecordState("phase", "combat", new JsonObject { ["floor"] = 1 });
        // Dedup must NOT move LatestStateSeq...
        session.RecordState("poll", "combat", new JsonObject { ["floor"] = 1 });
        // ...so an action recorded now links to s1, regardless of whether s1's
        // bytes have been physically flushed yet (they have not — no Flush).
        var a1 = session.RecordAction("hook:X", "end_turn", new JsonObject(), "executed", null);
        var s2 = session.RecordState("action", "combat", new JsonObject { ["floor"] = 2 });
        var a2 = session.RecordAction("hook:X", "play_card", new JsonObject(), "executed", null);
        session.Flush();

        Assert.True(a1 > s1 && a2 > s2);
        var actions = ReadJsonl(session.Directory, "actions");
        Assert.Equal(s1, actions[0].GetProperty("state_seq").GetInt64());
        Assert.Equal(s2, actions[1].GetProperty("state_seq").GetInt64());
    }

    // --- (b) stress: gapless/monotonic seq + per-stream FIFO + intact lines -----

    [Fact]
    public async Task ConcurrentRecordingKeepsPerStreamOrderAndGaplessSeq()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime, 0.0001));

        const int workers = 6;
        const int perWorker = 300;
        var tasks = Enumerable.Range(0, workers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWorker; i++)
            {
                // Globally-unique payloads ⇒ no two consecutive states dedup ⇒
                // the total count is deterministic.
                var tag = new JsonObject { ["w"] = w, ["i"] = i };
                switch (i % 3)
                {
                    case 0: session.RecordState("poll", "combat", tag); break;
                    case 1: session.RecordAction("hook:X", "play_card", tag, "executed", null); break;
                    default: session.RecordEvent("card_drawn", tag); break;
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        session.Flush();

        // Per-stream file order is ascending by seq (single FIFO writer thread).
        foreach (var stream in new[] { "states", "actions", "events" })
        {
            var seqs = ReadJsonl(session.Directory, stream)
                .Select(r => r.GetProperty("seq").GetInt64())
                .ToList();
            var sorted = seqs.OrderBy(s => s).ToList();
            Assert.Equal(sorted, seqs);
        }

        // Every line parsed cleanly above (no torn/interleaved writes), and the
        // union of seqs is exactly 1..N with no gaps or duplicates.
        var all = AllSeqsInOrder(session.Directory);
        Assert.Equal(workers * perWorker, all.Count);
        Assert.Equal(Enumerable.Range(1, all.Count).Select(n => (long)n), all);

        var counts = ReadManifest(session.Directory).GetProperty("counts");
        var total = counts.GetProperty("states").GetInt64()
            + counts.GetProperty("actions").GetInt64()
            + counts.GetProperty("events").GetInt64();
        Assert.Equal(all.Count, total);
    }
}
