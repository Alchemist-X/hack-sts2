using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>
/// Append-only JSONL writer over an explicit FileStream (one line per record).
/// Crash-safety contract: each record is written as one buffer in one Write
/// call on an unbuffered stream and flushed to the OS before WriteLine
/// returns, so a SIGKILL can only tear the single in-flight line — never a
/// previously written one.
/// </summary>
internal sealed class JsonlStreamWriter : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    public JsonlStreamWriter(string path)
    {
        // bufferSize: 1 disables FileStream's internal buffer (the documented
        // .NET convention). With the default 4096-byte buffer, consecutive
        // lines coalesce in process memory and reach the OS at buffer-full
        // boundaries that fall MID-line; a SIGKILL then loses the buffered
        // remainder and leaves a torn line at an arbitrary offset. Unbuffered,
        // every WriteLine hands its single line+newline buffer to the OS in
        // one write(2), so the torn-write window shrinks to the one in-flight
        // line at the instant of SIGKILL (unavoidable without O_APPEND
        // transactional semantics the platform does not offer).
        _stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 1);
    }

    /// <summary>Number of records written through this writer.</summary>
    public long LineCount { get; private set; }

    /// <summary>Uncompressed bytes written through this writer (perf counter).</summary>
    public long BytesWritten { get; private set; }

    public void WriteLine(JsonObject record)
    {
        // Torn-write contract: the whole record (line + trailing newline) is
        // materialized as ONE byte buffer and handed to the unbuffered
        // FileStream in a SINGLE Write call, then flushed to the OS. After
        // WriteLine returns, the complete line is in the kernel page cache,
        // which survives SIGKILL of the process (durability against power
        // loss is handled by the periodic Flush(flushToDisk: true)).
        var bytes = Encoding.UTF8.GetBytes(record.ToJsonString() + "\n");
        _stream.Write(bytes, 0, bytes.Length);
        _stream.Flush();
        LineCount++;
        BytesWritten += bytes.Length;
    }

    /// <summary>Flush buffered bytes through to the OS and disk.</summary>
    public void Flush()
    {
        if (!_disposed)
        {
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _stream.Flush(flushToDisk: true);
        }
        finally
        {
            _stream.Dispose();
        }
    }
}
