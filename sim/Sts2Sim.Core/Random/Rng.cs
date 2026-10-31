namespace Sts2Sim.Core.Random;

public readonly record struct RngSnapshot(int Counter, ulong S0, ulong S1, ulong S2, ulong S3) : ICanonical
{
    public override string ToString() => $"Counter: {Counter} State: {S0} {S1} {S2} {S3}";

    public void WriteCanonical(CanonicalWriter w) => Rng.WriteCanonicalState(w, Counter, S0, S1, S2, S3);
}

public sealed class Rng : ICanonical
{
    private int _counter;

    private readonly MegaRandom _random;

    public Rng(ulong seed = 0uL)
    {
        _random = new MegaRandom(seed);
    }

    public Rng(ulong seed, string name)
        : this(RngSeeds.Named(seed, name))
    {
    }

    public Rng(RngSnapshot snapshot)
    {
        _counter = snapshot.Counter;
        _random = new MegaRandom(snapshot);
    }

    public static Rng ForPlayerModel(ulong runSeed, int playerSlotIndex, string modelIdEntry, ulong mixin = 0uL)
    {
        return new Rng(RngSeeds.PlayerModel(runSeed, playerSlotIndex, modelIdEntry, mixin));
    }

    public static Rng ForPlayerModel(RunRngSet runRng, int playerSlotIndex, string modelIdEntry, ulong mixin = 0uL)
    {
        return ForPlayerModel(runRng.Seed, playerSlotIndex, modelIdEntry, mixin);
    }

    public int Counter => _counter;

    public MegaRandom Kernel => _random;

    public void LoadFromSnapshot(RngSnapshot snapshot)
    {
        _counter = snapshot.Counter;
        _random.Reinitialise(snapshot);
    }

    public bool NextBool()
    {
        _counter++;
        return _random.Next(2) == 0;
    }

    public int NextInt(int maxExclusive = int.MaxValue)
    {
        _counter++;
        return _random.Next(maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive), "Minimum must be lower than maximum.");
        }
        _counter++;
        return _random.Next(minInclusive, maxExclusive);
    }

    public uint NextUnsignedInt(uint maxExclusive = uint.MaxValue)
    {
        return NextUnsignedInt(0u, maxExclusive);
    }

    public uint NextUnsignedInt(uint minInclusive, uint maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive), "Minimum must be lower than maximum.");
        }
        _counter++;
        unchecked
        {
            double num = _random.NextDouble();
            double num2 = maxExclusive - minInclusive;
            uint num3 = (uint)(num * num2);
            return minInclusive + num3;
        }
    }

    public ulong NextUnsignedLong()
    {
        _counter++;
        return _random.NextULong();
    }

    public ulong NextUnsignedLong(ulong maxExclusive)
    {
        if (maxExclusive == ulong.MaxValue)
        {
            return NextUnsignedLong();
        }
        return NextUnsignedLong(0uL, maxExclusive);
    }

    public ulong NextUnsignedLong(ulong minInclusive, ulong maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive), "Minimum must be lower than maximum.");
        }
        _counter++;
        unchecked
        {
            double num = _random.NextDouble();
            double num2 = maxExclusive - minInclusive;
            ulong num3 = (ulong)(num * num2);
            return minInclusive + num3;
        }
    }

    public float NextFloat(float max = 1f)
    {
        return NextFloat(0f, max);
    }

    public float NextFloat(float min, float max)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum must not be higher than maximum.");
        }
        _counter++;
        return (float)(_random.NextDouble() * (double)(max - min) + (double)min);
    }

    public double NextDouble()
    {
        _counter++;
        return _random.NextDouble();
    }

    public double NextDouble(double min, double max)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum must not be higher than maximum.");
        }
        _counter++;
        return _random.NextDouble() * (max - min) + min;
    }

    public float NextGaussianFloat(float mean = 0f, float stdDev = 1f, float min = 0f, float max = 1f)
    {
        return (float)NextGaussianDouble(mean, stdDev, min, max);
    }

    public double NextGaussianDouble(double mean = 0.0, double stdDev = 1.0, double min = 0.0, double max = 1.0)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum must not be higher than maximum.");
        }
        if (mean < 0.0 || mean > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "Mean must be within [0, 1].");
        }
        double num6;
        do
        {
            double num = 1.0 - NextDouble();
            double num2 = 1.0 - NextDouble();
            double num3 = Math.Sqrt(-2.0 * Math.Log(num));
            double num4 = Math.PI * 2.0 * num2;
            double num5 = num3 * Math.Cos(num4);
            num6 = mean + num5 * stdDev;
        }
        while (num6 < 0.0 || num6 > 1.0);
        return num6 * (max - min) + min;
    }

    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum must not be higher than maximum.");
        }
        if (mean < min || mean > max)
        {
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "Mean must be within [min, max].");
        }
        int num5;
        do
        {
            double num = 1.0 - NextDouble();
            double num2 = 1.0 - NextDouble();
            double num3 = Math.Sqrt(-2.0 * Math.Log(num)) * Math.Sin(Math.PI * 2.0 * num2);
            double num4 = (double)mean + (double)stdDev * num3;
            num5 = (int)Math.Round(num4);
        }
        while (num5 < min || num5 > max);
        return num5;
    }

    public T? NextItem<T>(IReadOnlyList<T> items)
    {
        int num = items.Count;
        if (num == 0)
        {
            return default;
        }
        int num2 = NextInt(0, num);
        return items[num2];
    }

    public T? NextItem<T>(IEnumerable<T> items)
    {
        T[] array = (items as T[]) ?? items.ToArray();
        int num = array.Length;
        if (num == 0)
        {
            return default;
        }
        int num2 = NextInt(0, num);
        return array[num2];
    }

    public T? WeightedNextItem<T>(IReadOnlyList<T> items, Func<T?, float> weightFetcher)
    {
        float randInput = NextFloat();
        return WeightedNextItem<T?>(randInput, items, weightFetcher, default);
    }

    public T? WeightedNextItem<T>(IEnumerable<T> items, Func<T?, float> weightFetcher)
    {
        float randInput = NextFloat();
        return WeightedNextItem<T?>(randInput, items, weightFetcher, default);
    }

    public static T WeightedNextItem<T>(float randInput, IReadOnlyList<T> items, Func<T, float> weightFetcher, T fallback)
    {
        int count = items.Count;
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            sum += (double)weightFetcher(items[i]);
        }
        float num = (float)sum;
        float num2 = randInput * num;
        for (int i = 0; i < count; i++)
        {
            T current = items[i];
            num2 -= weightFetcher(current);
            if (num2 <= 0f)
            {
                return current;
            }
        }
        return fallback;
    }

    public static T WeightedNextItem<T>(float randInput, IEnumerable<T> items, Func<T, float> weightFetcher, T fallback)
    {
        float num = items.Sum(weightFetcher);
        float num2 = randInput * num;
        foreach (T current in items)
        {
            num2 -= weightFetcher(current);
            if (num2 <= 0f)
            {
                return current;
            }
        }
        return fallback;
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int num = list.Count - 1; num > 0; num--)
        {
            int num2 = NextInt(num + 1);
            int num3 = num;
            int num4 = num2;
            T val = list[num2];
            T val2 = list[num];
            list[num3] = val;
            list[num4] = val2;
        }
    }

    public RngSnapshot ToSnapshot() => _random.ToSnapshot(_counter);

    public Rng DeepClone() => new(ToSnapshot());

    public void WriteCanonical(CanonicalWriter w) =>
        WriteCanonicalState(w, _counter, _random.S0, _random.S1, _random.S2, _random.S3);

    internal static void WriteCanonicalState(CanonicalWriter w, int counter, ulong s0, ulong s1, ulong s2, ulong s3)
    {
        w.WriteTag(RngCanonicalTags.Rng);
        w.WriteInt32(counter);
        w.WriteUInt64(s0);
        w.WriteUInt64(s1);
        w.WriteUInt64(s2);
        w.WriteUInt64(s3);
    }
}
