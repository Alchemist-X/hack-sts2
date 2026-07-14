using System;
using System.Collections.Generic;
using System.IO;

namespace Sts2Recorder.Core;

/// <summary>
/// Rate-limited error sink writing to recorder.log inside the session directory.
/// Limits are per call site per time window: at most <see cref="MaxEntriesPerSite"/>
/// entries are logged per site per <see cref="WindowSeconds"/>. The first dropped
/// entry in a window logs one suppression notice; when the window expires and the
/// site errors again, the number of suppressed entries is reported and logging
/// resumes — so a later, different failure at the same site is never invisible.
/// Never throws: a failing error logger must not take the recorder down.
/// </summary>
internal sealed class ErrorLog
{
    public const int MaxEntriesPerSite = 5;
    public const double WindowSeconds = 600;

    private sealed record SiteWindow(double StartedAt, int Logged, long Suppressed);

    private readonly string _path;
    private readonly IClock _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, SiteWindow> _windowsBySite = new();

    public ErrorLog(string sessionDir, IClock clock)
    {
        _path = Path.Combine(sessionDir, "recorder.log");
        _clock = clock;
    }

    public void Record(string where, Exception exception)
    {
        lock (_lock)
        {
            var now = _clock.Now;
            var window = _windowsBySite.TryGetValue(where, out var existing)
                ? existing
                : null;
            if (window is not null && now - window.StartedAt >= WindowSeconds)
            {
                if (window.Suppressed > 0)
                {
                    Append(
                        now,
                        $"[{where}] {window.Suppressed} error(s) suppressed in the "
                        + $"previous {WindowSeconds:F0}s window");
                }
                window = null;
            }
            window ??= new SiteWindow(StartedAt: now, Logged: 0, Suppressed: 0);
            if (window.Logged < MaxEntriesPerSite)
            {
                var detail = OneLine($"{exception.GetType().Name}: {exception.Message}");
                Append(now, $"[{where}] {detail}");
                _windowsBySite[where] = window with { Logged = window.Logged + 1 };
                return;
            }
            if (window.Suppressed == 0)
            {
                Append(
                    now,
                    $"[{where}] further errors suppressed for {WindowSeconds:F0}s "
                    + $"(limit {MaxEntriesPerSite} per window)");
            }
            _windowsBySite[where] = window with { Suppressed = window.Suppressed + 1 };
        }
    }

    private void Append(double now, string message)
    {
        try
        {
            File.AppendAllText(
                _path,
                FormattableString.Invariant($"{now:F3} {message}\n"));
        }
        catch (Exception)
        {
            // Swallow: the error log must never throw into game code.
        }
    }

    private static string OneLine(string text) =>
        text.Replace("\r", " ").Replace("\n", " ");
}
