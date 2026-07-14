using System;

namespace Sts2Recorder.Core;

/// <summary>Real wall clock: unix epoch seconds from <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
