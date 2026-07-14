using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ParallelBeginsWithSameRunIdClaimDistinctDirectories()
    {
        // Regression: Directory.Exists + CreateDirectory is check-then-act;
        // two Begin() calls with the same run id must never share a session
        // directory (shared JSONL files, manifest temp-file races, leaked
        // FileStreams on the loser). The .claim file (CreateNew) arbitrates.
        var root = NewTempRoot();
        var meta = DefaultMeta();
        const int contenders = 8;
        using var barrier = new Barrier(contenders);
        var tasks = Enumerable.Range(0, contenders)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    return TrajectorySession.Begin(root, meta, new FakeClock(StartTime));
                },
                TaskCreationOptions.LongRunning))
            .ToArray();
        var sessions = await Task.WhenAll(tasks);
        try
        {
            var directories = sessions.Select(session => session.Directory).ToArray();
            Assert.Equal(contenders, directories.Distinct().Count());
            var parts = directories
                .Select(dir => ReadManifest(dir).GetProperty("part").GetInt32())
                .OrderBy(part => part);
            Assert.Equal(Enumerable.Range(1, contenders), parts);
            // Every winner must be independently usable.
            foreach (var session in sessions)
            {
                session.RecordEvent("card_drawn", new JsonObject { ["card"] = "CARD.ZAP" });
                session.Flush();
            }
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.Dispose();
            }
        }
    }

    [Fact]
    public async Task ParallelWritersProduceGaplessGlobalSeq()
    {
        var root = NewTempRoot();
        using var session = TrajectorySession.Begin(root, DefaultMeta(), new FakeClock(StartTime, 0.001));
        var anchor = session.RecordState(
            "phase", "combat", new JsonObject { ["screen"] = "combat", ["anchor"] = true });

        const int perWorker = 50;
        var workers = new List<Task>();
        for (var worker = 0; worker < 4; worker++)
        {
            var id = worker;
            workers.Add(Task.Run(() =>
            {
                for (var i = 0; i < perWorker; i++)
                {
                    switch ((id + i) % 3)
                    {
                        case 0:
                            session.RecordState(
                                "poll", "combat",
                                new JsonObject { ["worker"] = id, ["i"] = i });
                            break;
                        case 1:
                            session.RecordAction(
                                $"hook:Worker{id}", "play_card",
                                new JsonObject { ["i"] = i }, "executed", anchor);
                            break;
                        default:
                            session.RecordEvent(
                                "card_drawn", new JsonObject { ["worker"] = id, ["i"] = i });
                            break;
                    }
                }
            }));
        }
        await Task.WhenAll(workers);
        session.Flush();

        var seqs = AllSeqsInOrder(session.Directory);
        Assert.Equal(1 + 4 * perWorker, seqs.Count);
        // gapless and duplicate-free: 1..N exactly
        Assert.Equal(Enumerable.Range(1, seqs.Count).Select(n => (long)n), seqs);

        var manifest = ReadManifest(session.Directory);
        var counts = manifest.GetProperty("counts");
        var total =
            counts.GetProperty("states").GetInt64() +
            counts.GetProperty("actions").GetInt64() +
            counts.GetProperty("events").GetInt64();
        Assert.Equal(seqs.Count, total);
    }
}
