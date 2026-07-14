using Sts2Recorder.Core;

namespace Sts2Recorder.Core.Tests;

/// <summary>Deterministic clock: every read of Now advances by a fixed step.</summary>
public sealed class FakeClock : IClock
{
    private readonly object _lock = new();
    private readonly double _step;
    private double _current;

    public FakeClock(double start, double step = 0.25)
    {
        _current = start;
        _step = step;
    }

    public double Now
    {
        get
        {
            lock (_lock)
            {
                var value = _current;
                _current += _step;
                return value;
            }
        }
    }
}
