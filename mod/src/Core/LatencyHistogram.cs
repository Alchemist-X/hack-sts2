using System;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>Bounded-memory latency summary; percentile bounds have <=19% bucket width.</summary>
internal sealed class LatencyHistogram
{
    private readonly long[] _buckets = new long[96];
    public long Count { get; private set; }
    private double _sum;
    private double _max;

    public void Add(double ms)
    {
        ms = double.IsFinite(ms) ? Math.Max(0, ms) : 0;
        int bucket = ms <= .001 ? 0 : ms >= .001 * Math.Pow(2, 95 / 4.0) ? 95 :
            (int)Math.Ceiling(4 * Math.Log2(ms / .001));
        _buckets[bucket]++;
        Count++;
        _sum += ms;
        _max = Math.Max(_max, ms);
    }

    private double Percentile(double percentile)
    {
        long rank = (long)Math.Ceiling(Count * percentile), seen = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            seen += _buckets[i];
            if (seen >= rank)
                return i == 95 ? _max : Math.Min(_max, .001 * Math.Pow(2, i / 4.0));
        }
        return _max;
    }

    public JsonObject Describe() => new()
    {
        ["count"] = Count,
        ["avg"] = Count == 0 ? 0 : _sum / Count,
        ["max"] = _max,
        ["p95_upper_bound"] = Percentile(.95),
        ["p99_upper_bound"] = Percentile(.99),
    };
}
