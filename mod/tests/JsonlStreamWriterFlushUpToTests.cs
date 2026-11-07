using System.IO;
using System.Text.Json.Nodes;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// FlushUpTo contract: waits only until the CAPTURED line count has physically
/// reached the stream (then fsyncs), so a background flusher can bound its wait
/// even while new lines keep arriving; targets beyond the accepted count clamp;
/// a disposed writer is a safe no-op.
/// </summary>
public sealed class JsonlStreamWriterFlushUpToTests
{
    private static JsonObject Record(int seq) =>
        new() { ["seq"] = seq, ["type"] = "state" };

    [Fact]
    public void FlushUpTo_MakesCapturedLinesVisibleOnDisk()
    {
        var path = Path.Combine(NewTempRoot(), "states.jsonl");
        using var writer = new JsonlStreamWriter(path);
        for (var i = 1; i <= 3; i++)
        {
            writer.WriteLine(Record(i));
        }
        var (lines, bytes) = writer.Counts();
        Assert.Equal(3, lines);
        Assert.True(bytes > 0);

        writer.FlushUpTo(lines);

        Assert.Equal(3, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void FlushUpTo_ClampsTargetsBeyondAcceptedCount()
    {
        var path = Path.Combine(NewTempRoot(), "states.jsonl");
        using var writer = new JsonlStreamWriter(path);
        writer.WriteLine(Record(1));

        writer.FlushUpTo(1000);

        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void FlushUpTo_OnDisposedWriterIsANoOp()
    {
        var path = Path.Combine(NewTempRoot(), "states.jsonl");
        var writer = new JsonlStreamWriter(path);
        writer.WriteLine(Record(1));
        writer.Dispose();

        writer.FlushUpTo(1);

        Assert.Single(File.ReadAllLines(path));
    }
}
