using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Torn-write contract (SIGKILL mid-run): every WriteLine must hand its whole
/// line + newline to the OS before returning — no .NET-internal buffering that
/// coalesces lines and tears one at a 4096-byte buffer boundary on SIGKILL.
/// The tests read the file through a second handle WITHOUT calling
/// Flush()/Dispose() on the writer, so any bytes still held in a FileStream
/// buffer would be invisible and fail the assertions.
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
        // FileShare.Read on the writer allows a concurrent reader; what this
        // sees is exactly what would survive a SIGKILL of the writer process.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void LineIsOnDiskImmediatelyAfterWriteLineWithoutFlush()
    {
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        var record = Record(seq: 1, payloadChars: 100);

        writer.WriteLine(record);

        var onDisk = ReadAllThroughSecondHandle(path);
        Assert.Equal(record.ToJsonString() + "\n", onDisk);
    }

    [Fact]
    public void LinesLargerThanDefaultFileStreamBufferAreNeverSplit()
    {
        // Regression for the observed torn write: ~2.7 KiB state lines were
        // coalesced by the default 4096-byte FileStream buffer and cut
        // mid-line at buffer-full boundaries. Multiple > 4 KiB lines with no
        // explicit flush must all be complete on disk after each WriteLine.
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        for (var seq = 1; seq <= 5; seq++)
        {
            writer.WriteLine(Record(seq, payloadChars: 5000));
        }

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
    public void CountersTrackWholeLines()
    {
        var path = NewJsonlPath();
        using var writer = new JsonlStreamWriter(path);
        var record = Record(seq: 1, payloadChars: 10);
        writer.WriteLine(record);
        writer.WriteLine(Record(seq: 2, payloadChars: 10));

        Assert.Equal(2, writer.LineCount);
        Assert.Equal(new FileInfo(path).Length, writer.BytesWritten);
    }
}
