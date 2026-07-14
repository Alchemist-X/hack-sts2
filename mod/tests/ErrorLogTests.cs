using System;
using System.IO;
using System.Linq;
using Sts2Recorder.Core;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

public sealed class ErrorLogTests
{
    /// <summary>Clock whose time only moves when the test says so.</summary>
    private sealed class ManualClock : IClock
    {
        public double Current { get; set; }
        public double Now => Current;
    }

    private static string[] LogLines(string dir) =>
        File.ReadAllLines(Path.Combine(dir, "recorder.log"));

    [Fact]
    public void CapAppliesPerSitePerWindow()
    {
        var dir = NewTempRoot();
        var clock = new ManualClock { Current = 1000.0 };
        var log = new ErrorLog(dir, clock);
        for (var i = 0; i < 10; i++)
        {
            log.Record("hook:X", new InvalidOperationException($"boom {i}"));
        }
        var lines = LogLines(dir);
        // MaxEntriesPerSite entries + exactly one suppression notice
        Assert.Equal(ErrorLog.MaxEntriesPerSite + 1, lines.Length);
        Assert.Single(lines, line => line.Contains("suppressed"));
    }

    [Fact]
    public void WindowExpiryResumesLoggingAndReportsSuppressedCount()
    {
        var dir = NewTempRoot();
        var clock = new ManualClock { Current = 1000.0 };
        var log = new ErrorLog(dir, clock);
        for (var i = 0; i < 10; i++)
        {
            log.Record("hook:X", new InvalidOperationException($"early {i}"));
        }
        // A later, DIFFERENT failure at the same site must not be invisible.
        clock.Current += ErrorLog.WindowSeconds + 1.0;
        log.Record("hook:X", new InvalidOperationException("late failure"));

        var lines = LogLines(dir);
        Assert.Contains(lines, line => line.Contains("late failure"));
        Assert.Contains(
            lines, line => line.Contains("5 error(s) suppressed in the previous"));
        // 5 early + 1 notice + 1 suppressed-summary + 1 late = 8
        Assert.Equal(8, lines.Length);
    }

    [Fact]
    public void WindowExpiryWithoutSuppressionEmitsNoSummary()
    {
        var dir = NewTempRoot();
        var clock = new ManualClock { Current = 1000.0 };
        var log = new ErrorLog(dir, clock);
        log.Record("hook:X", new InvalidOperationException("first"));
        clock.Current += ErrorLog.WindowSeconds + 1.0;
        log.Record("hook:X", new InvalidOperationException("second"));

        var lines = LogLines(dir);
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(lines, line => line.Contains("suppressed"));
    }

    [Fact]
    public void SitesAreRateLimitedIndependently()
    {
        var dir = NewTempRoot();
        var clock = new ManualClock { Current = 1000.0 };
        var log = new ErrorLog(dir, clock);
        for (var i = 0; i < 10; i++)
        {
            log.Record("hook:A", new InvalidOperationException($"a {i}"));
        }
        log.Record("hook:B", new InvalidOperationException("b"));
        var lines = LogLines(dir);
        Assert.Single(lines, line => line.Contains("hook:B"));
    }
}
