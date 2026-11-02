using System.Diagnostics;
using System.Runtime;
using System.Text;
using Sts2Sim.Vec;

namespace Sts2Sim.Bench;

/// <summary>
/// <c>dotnet run -c Release --project sim/Sts2Sim.Bench -- vec [envs=1024] [threads=8] [steps=500] [ascension=0] [pool=1024]</c>.
/// Drives a VecEnv in-process through the real C ABI entry points (delegate* unmanaged to the [UnmanagedCallersOnly]
/// exports) with NativeMemory buffers and a seeded uniform-random candidate policy. Pool templates are warmed before the
/// timed window (NewRun ≈ 16 ms each would otherwise dominate). Run with DOTNET_gcServer=1 to match the shipped
/// sts2vec.runtimeconfig.json (Server GC); the GC mode is printed.
/// </summary>
public static unsafe class VecBench
{
    public static int Run(string[] args)
    {
        int Arg(int i, int dflt) => args.Length > i && int.TryParse(args[i], out var v) ? v : dflt;
        int envs = Arg(1, 1024);
        int threads = Arg(2, 8);
        int steps = Arg(3, 500);
        int ascension = Arg(4, 0);
        int poolCount = Arg(5, 1024);
        const int warmupSteps = 20;

        delegate* unmanaged<byte*, int, long> create = &Exports.Create;
        delegate* unmanaged<long, int> destroy = &Exports.Destroy;
        delegate* unmanaged<long, byte*, int*, void**, int> reset = &Exports.Reset;
        delegate* unmanaged<long, int*, void**, int> step = &Exports.Step;
        delegate* unmanaged<long, int, byte*, int, int> lastError = &Exports.LastError;

        bool server = GCSettings.IsServerGC;
        if (!server)
        {
            Exports.RequireServerGC = false;
        }
        string config = $"{{\"num_envs\":{envs},\"threads\":{threads},\"pool\":{{\"fresh\":{{\"characters\":[\"IRONCLAD\",\"SILENT\",\"DEFECT\",\"REGENT\",\"NECROBINDER\"],\"ascension\":{ascension},\"count\":{poolCount},\"seed_prefix\":\"BENCH\"}}}}}}";
        long h;
        var cfg = Encoding.UTF8.GetBytes(config);
        fixed (byte* p = cfg)
        {
            h = create(p, cfg.Length);
        }
        if (h <= 0)
        {
            Console.WriteLine("vec: create failed: " + Exports.GlobalError);
            return 1;
        }
        long bytesPerEnv = 0;
        for (int k = 0; k < Buf.Count; k++)
        {
            bytesPerEnv += Schema.BytesPerEnv(k);
        }
        Console.WriteLine($"vec: envs={envs} threads={threads} steps={steps} ascension={ascension} pool={poolCount} gc={(server ? "server" : "workstation")} " +
            $"latency={GCSettings.LatencyMode} bytes/env={bytesPerEnv} encoder={Schema.EncoderVersion} cpus={Environment.ProcessorCount}");

        var env = Exports.EnvOf(h)!;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, env.Pool.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(threads, Environment.ProcessorCount) }, i => env.Pool.Materialize(i));
        Console.WriteLine($"vec: pool warm {env.Pool.Count} templates in {sw.Elapsed.TotalMilliseconds:F0} ms ({env.Pool.CachedTemplates} cached)");

        using var nb = new NativeBuffers(envs);
        var actions = new int[envs];
        var rng = new ulong[envs];
        for (int i = 0; i < envs; i++)
        {
            rng[i] = 0x9E3779B97F4A7C15UL * (ulong)(i + 1);
        }
        var candN = nb.All<int>(Buf.CandN);
        var status = nb.All<int>(Buf.Status);
        var term = nb.All<byte>(Buf.Terminated);
        var trunc = nb.All<byte>(Buf.Truncated);
        var reward = nb.All<float>(Buf.Reward);

        sw.Restart();
        if (reset(h, null, null, nb.Table) != 0)
        {
            Console.WriteLine("vec: reset failed: " + Exports.GlobalError);
            return 1;
        }
        double resetMs = sw.Elapsed.TotalMilliseconds;

        long decisions = 0, resets = 0, wins = 0, ends = 0, truncs = 0, errors = 0, gaps = 0, illegal = 0;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        double minMs = double.MaxValue, maxMs = 0;
        var batch = new Stopwatch();
        var total = new Stopwatch();
        for (int t = -warmupSteps; t < steps; t++)
        {
            for (int i = 0; i < envs; i++)
            {
                int n = candN[i];
                if (n <= 0)
                {
                    actions[i] = -1;
                    continue;
                }
                ulong x = rng[i];
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                rng[i] = x;
                actions[i] = (int)(x % (ulong)n);
            }
            if (t == 0)
            {
                gen0 = GC.CollectionCount(0);
                gen1 = GC.CollectionCount(1);
                gen2 = GC.CollectionCount(2);
                total.Start();
            }
            batch.Restart();
            int rc;
            fixed (int* a = actions)
            {
                rc = step(h, a, nb.Table);
            }
            batch.Stop();
            if (rc != 0)
            {
                Console.WriteLine("vec: step failed: " + Exports.GlobalError);
                return 1;
            }
            if (t < 0)
            {
                continue;
            }
            double ms = batch.Elapsed.TotalMilliseconds;
            minMs = Math.Min(minMs, ms);
            maxMs = Math.Max(maxMs, ms);
            for (int i = 0; i < envs; i++)
            {
                if (actions[i] >= 0)
                {
                    decisions++;
                }
                var st = (SlotStatus)status[i];
                if ((st & SlotStatus.Reset) != 0) resets++;
                if ((st & SlotStatus.SimError) != 0) errors++;
                if ((st & SlotStatus.ContentGap) != 0) gaps++;
                if ((st & SlotStatus.IllegalAction) != 0) illegal++;
                if (term[i] != 0) { ends++; if (reward[i] > 0f) wins++; }
                if (trunc[i] != 0) truncs++;
            }
        }
        total.Stop();
        double secs = total.Elapsed.TotalSeconds;
        Console.WriteLine($"vec: initial reset {resetMs:F1} ms; timed {steps} batches in {secs:F2} s");
        Console.WriteLine($"vec: env-steps/s={decisions / secs:F0} ms/batch avg={total.Elapsed.TotalMilliseconds / steps:F3} min={minMs:F3} max={maxMs:F3} " +
            $"resets/s={resets / secs:F0} episodes ended={ends} (wins {wins}) truncated={truncs} sim_error={errors} content_gap={gaps} illegal={illegal}");
        Console.WriteLine($"vec: gc collections during window gen0={GC.CollectionCount(0) - gen0} gen1={GC.CollectionCount(1) - gen1} gen2={GC.CollectionCount(2) - gen2}; " +
            $"obs MB/batch={bytesPerEnv * envs / 1e6:F1}");
        if (errors > 0)
        {
            for (int i = 0; i < envs; i++)
            {
                int n = lastError(h, i, null, 0);
                if (n > 0)
                {
                    var buf = new byte[n];
                    fixed (byte* p = buf)
                    {
                        lastError(h, i, p, n);
                    }
                    Console.WriteLine($"vec: slot {i} last error: {Encoding.UTF8.GetString(buf).Split('\n')[0]}");
                    break;
                }
            }
        }
        destroy(h);
        return 0;
    }
}
