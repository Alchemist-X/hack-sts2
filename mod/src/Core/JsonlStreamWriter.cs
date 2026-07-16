using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sts2Recorder.Core;

/// <summary>
/// Append-only JSONL writer whose physical write+flush syscall runs on a single
/// dedicated background thread, draining a FIFO queue. The caller thread only
/// enqueues an already-materialized line buffer and returns immediately, so the
/// Godot main thread never pays the write(2)+flush cost inline.
///
/// Ordering: a single writer thread consuming one FIFO queue guarantees that
/// lines reach the file in enqueue order. Callers enqueue under the session lock
/// in seq order, so per-stream file order == seq order.
///
/// Torn-write / crash-salvage contract (unchanged from the synchronous version,
/// just moved to the writer thread): each line is written as ONE byte buffer in
/// ONE <see cref="FileStream.Write(byte[],int,int)"/> on an unbuffered stream and
/// flushed to the OS per line, so a SIGKILL can only tear the single in-flight
/// line. Bytes still sitting in the in-memory queue (not yet handed to Write) are
/// lost on a hard crash — which is acceptable ONLY because <see cref="Flush"/>
/// blocks until the queue is fully drained AND fsync'd, bounding the loss window
/// to one heartbeat. <see cref="Dispose"/> drains + joins the writer thread and
/// fsyncs before closing.
///
/// Resilience: a fault on the writer thread is captured (never thrown into game
/// code) and the writer falls back to synchronous, in-order writes on the caller
/// thread so no queued line is silently dropped.
///
/// Two locks with distinct roles:
///  • <c>_mutex</c> guards the queue, counters, and writer-thread signaling; it is
///    the only lock a normal (fault-free) enqueue ever takes, so producers never
///    block on I/O.
///  • <c>_ioLock</c> serializes every physical <see cref="FileStream"/> operation
///    (writer-thread Write, fsync in Flush, close in Dispose, sync fallback), so a
///    fsync can never overlap a Write.
/// </summary>
internal sealed class JsonlStreamWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _mutex = new();
    private readonly object _ioLock = new();
    private readonly Queue<byte[]> _pending = new();
    private readonly Action<string, Exception>? _onError;
    private readonly Thread _thread;

    // Lines accepted by Enqueue (synchronous, at enqueue time) vs lines whose
    // bytes have physically reached the stream. Flush waits for these to meet.
    private long _lineCount;
    private long _bytesWritten;
    private long _writtenThrough;

    // The one item the writer thread had dequeued when a write faulted; kept so
    // the synchronous fallback can re-attempt it ahead of the rest of _pending.
    private byte[]? _faultedItem;
    private Exception? _fault;
    private bool _shutdown;
    private bool _disposed;

    public JsonlStreamWriter(string path, Action<string, Exception>? onError = null)
    {
        // bufferSize: 1 disables FileStream's internal buffer (the documented
        // .NET convention). With the default 4096-byte buffer, consecutive lines
        // coalesce in process memory and reach the OS at buffer-full boundaries
        // that fall MID-line; a SIGKILL then loses the buffered remainder and
        // leaves a torn line. Unbuffered, every Write hands its single line+newline
        // buffer to the OS in one write(2), so the torn-write window shrinks to the
        // one in-flight line at the instant of SIGKILL.
        _stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 1);
        _onError = onError;
        _thread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = $"jsonl-writer:{Path.GetFileName(path)}",
        };
        _thread.Start();
    }

    /// <summary>Number of records accepted by this writer (updated at enqueue).</summary>
    public long LineCount
    {
        get
        {
            lock (_mutex)
            {
                return _lineCount;
            }
        }
    }

    /// <summary>Uncompressed bytes accepted by this writer (updated at enqueue).</summary>
    public long BytesWritten
    {
        get
        {
            lock (_mutex)
            {
                return _bytesWritten;
            }
        }
    }

    /// <summary>
    /// Enqueues one already-materialized line buffer (line + trailing newline) for
    /// the writer thread. Returns immediately after enqueue; the counters are
    /// updated synchronously so a manifest rewrite that follows a drain sees exact
    /// counts. Never blocks on I/O and never throws into game code.
    /// </summary>
    public void Enqueue(byte[] lineBytes)
    {
        ArgumentNullException.ThrowIfNull(lineBytes);
        lock (_mutex)
        {
            _lineCount++;
            _bytesWritten += lineBytes.Length;
            if (_fault is not null)
            {
                // Writer thread is dead: preserve order by flushing everything it
                // left behind, then this line, synchronously on the caller thread.
                DrainFaultedLocked();
                WriteSyncLocked(lineBytes);
                return;
            }
            _pending.Enqueue(lineBytes);
            Monitor.Pulse(_mutex);
        }
    }

    /// <summary>
    /// Convenience for tests and non-hot paths: serialize a record and enqueue it.
    /// The hot recorder path builds line bytes via <see cref="JsonlLine"/> and calls
    /// <see cref="Enqueue"/> directly.
    /// </summary>
    public void WriteLine(JsonObject record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Enqueue(Encoding.UTF8.GetBytes(record.ToJsonString() + "\n"));
    }

    /// <summary>
    /// Blocks until every enqueued line has been written, then fsyncs. Called on
    /// SaveManager.Saved heartbeats and before every manifest rewrite, this bounds
    /// the crash-loss window to the lines enqueued since the last Flush.
    /// </summary>
    public void Flush()
    {
        lock (_mutex)
        {
            if (_disposed)
            {
                return;
            }
            DrainLocked();
        }
        // fsync outside _mutex (so enqueues aren't blocked by disk latency) but
        // under _ioLock: after DrainLocked the writer thread is idle (queue empty)
        // and, because callers hold the session lock across Flush, no new line can
        // be enqueued meanwhile — the fsync cannot race a Write.
        lock (_ioLock)
        {
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        lock (_mutex)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _shutdown = true;
            Monitor.PulseAll(_mutex);
        }
        // Join: the writer thread drains all remaining queued lines before it
        // observes _shutdown with an empty queue and exits.
        _thread.Join();
        lock (_mutex)
        {
            // A fault could have left the thread's leftovers unwritten; the
            // synchronous fallback finishes them so nothing is dropped on close.
            if (_writtenThrough < _lineCount)
            {
                DrainFaultedLocked();
            }
        }
        lock (_ioLock)
        {
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

    /// <summary>Waits (or synchronously drains on fault) until _writtenThrough == _lineCount.</summary>
    private void DrainLocked()
    {
        while (_writtenThrough < _lineCount && _fault is null)
        {
            Monitor.Wait(_mutex);
        }
        if (_writtenThrough < _lineCount)
        {
            // Fault surfaced while lines were still queued: finish them here.
            DrainFaultedLocked();
        }
    }

    private void WriterLoop()
    {
        while (true)
        {
            byte[] item;
            lock (_mutex)
            {
                while (_pending.Count == 0 && !_shutdown)
                {
                    Monitor.Wait(_mutex);
                }
                if (_pending.Count == 0)
                {
                    // _shutdown with an empty queue: everything is drained.
                    return;
                }
                item = _pending.Dequeue();
            }
            try
            {
                WriteToStream(item);
                lock (_mutex)
                {
                    _writtenThrough++;
                    Monitor.PulseAll(_mutex);
                }
            }
            catch (Exception ex)
            {
                lock (_mutex)
                {
                    // Hold the item so the sync fallback re-attempts it ahead of
                    // the still-queued lines, preserving order.
                    _faultedItem = item;
                    _fault = ex;
                    Monitor.PulseAll(_mutex);
                }
                _onError?.Invoke("JsonlStreamWriter.WriterLoop", ex);
                return;
            }
        }
    }

    /// <summary>
    /// Writes the writer thread's leftovers synchronously, in order: the faulted
    /// in-flight item first, then the rest of the queue. Best effort — a write that
    /// still fails is reported (never thrown) so no data is silently dropped.
    /// Must be called under <c>_mutex</c>.
    /// </summary>
    private void DrainFaultedLocked()
    {
        if (_faultedItem is { } faulted)
        {
            _faultedItem = null;
            WriteSyncLocked(faulted);
        }
        while (_pending.Count > 0)
        {
            WriteSyncLocked(_pending.Dequeue());
        }
    }

    /// <summary>Synchronous, fault-tolerant write of one line. Must hold <c>_mutex</c>.</summary>
    private void WriteSyncLocked(byte[] item)
    {
        try
        {
            WriteToStream(item);
            _writtenThrough++;
        }
        catch (Exception ex)
        {
            // The disk is genuinely failing; there is nothing more we can do than
            // surface it. Never rethrow into game code.
            _onError?.Invoke("JsonlStreamWriter.WriteSync", ex);
        }
    }

    private void WriteToStream(byte[] item)
    {
        lock (_ioLock)
        {
            _stream.Write(item, 0, item.Length);
            // Per-line OS flush (not fsync): the complete line lands in the kernel
            // page cache and survives a SIGKILL of the process. Durability against
            // power loss is the periodic Flush(flushToDisk: true).
            _stream.Flush();
        }
    }
}
