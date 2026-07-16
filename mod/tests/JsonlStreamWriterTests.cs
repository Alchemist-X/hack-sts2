using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Async ordered-writer contract. The write+flush syscall runs on a dedicated
/// background thread draining a FIFO queue, so a line reaches disk after
/// Flush()/Dispose() (which block until the queue is drained + fsync'd) rather
/// than synchronously inside WriteLine. The torn-write guarantee still holds on
/// the writer thread: each line is one buffer in one Write + per-line OS flush,
/// so no line is ever split at a buffer boundary — verified by reading through a
/// second handle after a drain.
/// </summary>
public sealed class JsonlStreamWriterTests
{
    private static string NewJsonlPath() =>
        Path.Combine(NewTempRoot(), "states.jsonl");

    private static JsonObject Record(int seq, int payloadChars)
    {
        return new JsonObject
        {
            ["seq"] = seq,
            ["t"] = 1000.0 + seq,
            ["type"] = "state",
            ["state"] = new JsonObject
            {
                ["blob"] = new string('x', payloadChars),
            },
        };
    }

    private static string ReadAllThroughSecondHandle(string path)
    {
        // FileShare.Read on the writer allows a concurrent reader; what this sees
        // after a drain is exactly what would survive a SIGKILL of the process.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void LineIsOnDiskAfterFlush()
    {
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        var record = Record(seq: 1, payloadChars: 100);

        writer.WriteLine(record);
        writer.Flush(); // blocks until the queue is drained + fsync'd

        var onDisk = ReadAllThroughSecondHandle(path);
        Assert.Equal(record.ToJsonString() + "\n", onDisk);
    }

    [Fact]
    public void LinesLargerThanDefaultFileStreamBufferAreNeverSplit()
    {
        // Regression for the observed torn write: ~2.7 KiB state lines were
        // coalesced by the default 4096-byte FileStream buffer and cut mid-line
        // at buffer-full boundaries. Multiple > 4 KiB lines must all be complete
        // on disk after the drain.
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        for (var seq = 1; seq <= 5; seq++)
        {
            writer.WriteLine(Record(seq, payloadChars: 5000));
        }
        writer.Flush();

        var lines = ReadAllThroughSecondHandle(path)
            .Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        foreach (var line in lines)
        {
            // Every line parses: no line was torn at a buffer boundary.
            Assert.NotNull(JsonNode.Parse(line));
        }
    }

    [Fact]
    public void CountersAreUpdatedSynchronouslyAtEnqueue()
    {
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        writer.WriteLine(Record(seq: 1, payloadChars: 10));
        writer.WriteLine(Record(seq: 2, payloadChars: 10));

        // Counts reflect accepted lines immediately, before any drain — so a
        // manifest rewrite that follows Flush() sees exact counts.
        Assert.Equal(2, writer.LineCount);

        writer.Flush();
        Assert.Equal(new FileInfo(path).Length, writer.BytesWritten);
    }

    [Fact]
    public void DisposeDrainsQueuedLinesToDisk()
    {
        // Invariant: Dispose() drains + joins the writer thread and fsyncs, so a
        // burst of lines followed immediately by Dispose (no explicit Flush) all
        // reach disk.
        var path = NewJsonlPath();
        const int n = 200;
        using (var writer = new JsonlStreamWriter(path))
        {
            for (var seq = 1; seq <= n; seq++)
            {
                writer.WriteLine(Record(seq, payloadChars: 40));
            }
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(n, lines.Length);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(i + 1, JsonNode.Parse(lines[i])!["seq"]!.GetValue<int>());
        }
    }

    [Fact]
    public void EnqueuedLinesReachDiskInFifoOrder()
    {
        // The single writer thread consuming one FIFO queue guarantees file order
        // == enqueue order.
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        for (var seq = 1; seq <= 50; seq++)
        {
            writer.Enqueue(Encoding.UTF8.GetBytes($"{{\"seq\":{seq}}}\n"));
        }
        writer.Flush();

        var lines = File.ReadAllLines(path);
        Assert.Equal(50, lines.Length);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(i + 1, JsonNode.Parse(lines[i])!["seq"]!.GetValue<int>());
        }
    }
}
