using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Sts2Recorder.Core;

/// <summary>
/// Off-thread writer for native snapshot copies (native/saves, native/replays).
/// The caller hands over bytes it already read; one background thread writes
/// each job to a temp file, fsyncs it and renames it onto the final path with
/// no overwrite. A failing job is reported and dropped; it never throws into
/// the caller. Dispose drains every queued job before returning.
/// </summary>
internal sealed class NativeFileWriter : IDisposable
{
    private const string TempSuffix = ".tmp";

    private readonly object _mutex = new();
    private readonly Queue<(string Path, byte[] Bytes)> _pending = new();
    private readonly Action<string, Exception>? _onError;
    private Thread? _thread;
    private long _enqueued;
    private long _finished;
    private long _failed;
    private bool _shutdown;
    private bool _disposed;

    public NativeFileWriter(Action<string, Exception>? onError = null)
    {
        _onError = onError;
    }

    /// <summary>Jobs accepted so far.</summary>
    public long Enqueued
    {
        get
        {
            lock (_mutex)
            {
                return _enqueued;
            }
        }
    }

    /// <summary>Jobs whose file reached its final path.</summary>
    public long Written
    {
        get
        {
            lock (_mutex)
            {
                return _finished - _failed;
            }
        }
    }

    /// <summary>Jobs that failed (reported through the error callback).</summary>
    public long Failed
    {
        get
        {
            lock (_mutex)
            {
                return _failed;
            }
        }
    }

    /// <summary>Queues one file; never blocks on I/O. Throws only after Dispose.</summary>
    public void Enqueue(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        lock (_mutex)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NativeFileWriter));
            }
            _enqueued++;
            _pending.Enqueue((path, bytes));
            if (_thread == null)
            {
                _thread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "native-writer",
                };
                _thread.Start();
            }
            Monitor.PulseAll(_mutex);
        }
    }

    /// <summary>Waits until every queued job has finished; false on timeout.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)Math.Max(0, timeout.TotalMilliseconds);
        lock (_mutex)
        {
            while (_finished < _enqueued)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                {
                    return false;
                }
                Monitor.Wait(_mutex, (int)Math.Min(remaining, int.MaxValue));
            }
            return true;
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_mutex)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _shutdown = true;
            thread = _thread;
            Monitor.PulseAll(_mutex);
        }
        thread?.Join();
    }

    private void WriterLoop()
    {
        while (true)
        {
            (string Path, byte[] Bytes) job;
            lock (_mutex)
            {
                while (_pending.Count == 0 && !_shutdown)
                {
                    Monitor.Wait(_mutex);
                }
                if (_pending.Count == 0)
                {
                    return;
                }
                job = _pending.Dequeue();
            }
            var ok = TryWrite(job.Path, job.Bytes);
            lock (_mutex)
            {
                _finished++;
                if (!ok)
                {
                    _failed++;
                }
                Monitor.PulseAll(_mutex);
            }
        }
    }

    private bool TryWrite(string path, byte[] bytes)
    {
        var temp = path + TempSuffix;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            Report(ex);
            return false;
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            _onError?.Invoke("NativeFileWriter.Write", ex);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
