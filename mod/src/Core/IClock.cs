namespace Sts2Recorder.Core;

/// <summary>Injectable time source. <see cref="Now"/> is unix epoch seconds (float).</summary>
public interface IClock
{
    double Now { get; }
}
