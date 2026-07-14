using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>Append-only JSONL writer over an explicit FileStream (one line per record).</summary>
internal sealed class JsonlStreamWriter : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    public JsonlStreamWriter(string path)
    {
        _stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    /// <summary>Number of records written through this writer.</summary>
    public long LineCount { get; private set; }

    public void WriteLine(JsonObject record)
    {
        var bytes = Encoding.UTF8.GetBytes(record.ToJsonString() + "\n");
        _stream.Write(bytes, 0, bytes.Length);
        LineCount++;
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
