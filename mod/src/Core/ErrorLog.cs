using System;
using System.Collections.Generic;
using System.IO;

namespace Sts2Recorder.Core;

/// <summary>
/// Rate-limited error sink writing to recorder.log inside the session directory.
/// At most <see cref="MaxEntriesPerSite"/> entries per call site are logged; one
/// suppression notice follows, then further errors from that site are dropped.
/// Never throws: a failing error logger must not take the recorder down.
/// </summary>
internal sealed class ErrorLog
{
    public const int MaxEntriesPerSite = 5;

    private readonly string _path;
    private readonly IClock _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _countsBySite = new();

    public ErrorLog(string sessionDir, IClock clock)
    {
        _path = Path.Combine(sessionDir, "recorder.log");
        _clock = clock;
    }

    public void Record(string where, Exception exception)
    {
        lock (_lock)
        {
            var count = _countsBySite.TryGetValue(where, out var existing) ? existing : 0;
            _countsBySite[where] = count + 1;
            if (count >= MaxEntriesPerSite)
            {
                if (count == MaxEntriesPerSite)
                {
                    Append($"[{where}] further errors suppressed (limit {MaxEntriesPerSite})");
                }
                return;
            }
            var detail = OneLine($"{exception.GetType().Name}: {exception.Message}");
            Append($"[{where}] {detail}");
        }
    }

    private void Append(string message)
    {
        try
        {
            File.AppendAllText(
                _path,
                FormattableString.Invariant($"{_clock.Now:F3} {message}\n"));
        }
        catch (Exception)
        {
            // Swallow: the error log must never throw into game code.
        }
    }

    private static string OneLine(string text) =>
        text.Replace("\r", " ").Replace("\n", " ");
}
