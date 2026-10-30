using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sts2Sim.Core.Random;

public sealed class MegaRandom : ICanonical
{
    private const double IncrDouble = 1.1102230246251565E-16;

    private const float IncrFloat = 5.9604645E-08f;

    private ulong _s0;

    private ulong _s1;

    private ulong _s2;

    private ulong _s3;

    public MegaRandom(ulong seed)
    {
        Reinitialise(seed);
    }

    public MegaRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        Reinitialise(s0, s1, s2, s3);
    }

    public MegaRandom(RngSnapshot snapshot)
    {
        Reinitialise(snapshot);
    }

    public ulong S0 => _s0;

    public ulong S1 => _s1;

    public ulong S2 => _s2;

    public ulong S3 => _s3;

    public static ulong Splitmix64(ref ulong x)
    {
        unchecked
        {
            ulong num = (x += 11400714819323198485uL);
            num = (num ^ (num >> 30)) * 13787848793156543929uL;
            num = (num ^ (num >> 27)) * 10723151780598845931uL;
            return num ^ (num >> 31);
        }
    }

    public void Reinitialise(ulong seed)
    {
        _s0 = Splitmix64(ref seed);
        _s1 = Splitmix64(ref seed);
        _s2 = Splitmix64(ref seed);
        _s3 = Splitmix64(ref seed);
    }

    public void Reinitialise(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    public void Reinitialise(RngSnapshot snapshot)
    {
        Reinitialise(snapshot.S0, snapshot.S1, snapshot.S2, snapshot.S3);
    }

    public void NextBytes(Span<byte> span)
    {
        unchecked
        {
            ulong num = _s0;
            ulong num2 = _s1;
            ulong num3 = _s2;
            ulong num4 = _s3;
            while (span.Length >= 8)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span, BitOperations.RotateLeft(num2 * 5, 7) * 9);
                ulong num5 = num2 << 17;
                num3 ^= num;
                num4 ^= num2;
                num2 ^= num3;
                num ^= num4;
                num3 ^= num5;
                num4 = BitOperations.RotateLeft(num4, 45);
                span = span.Slice(8);
            }
            if (!span.IsEmpty)
            {
                ulong num6 = BitOperations.RotateLeft(num2 * 5, 7) * 9;
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = (byte)(num6 >> (8 * i));
                }
                ulong num7 = num2 << 17;
                num3 ^= num;
                num4 ^= num2;
                num2 ^= num3;
                num ^= num4;
                num3 ^= num7;
                num4 = BitOperations.RotateLeft(num4, 45);
            }
            _s0 = num;
            _s1 = num2;
            _s2 = num3;
            _s3 = num4;
        }
    }

    private ulong NextULongInner()
    {
        unchecked
        {
            ulong s = _s0;
            ulong s2 = _s1;
            ulong s3 = _s2;
            ulong s4 = _s3;
            ulong result = BitOperations.RotateLeft(s2 * 5, 7) * 9;
            ulong num = s2 << 17;
            s3 ^= s;
            s4 ^= s2;
            s2 ^= s3;
            s ^= s4;
            s3 ^= num;
            s4 = BitOperations.RotateLeft(s4, 45);
            _s0 = s;
            _s1 = s2;
            _s2 = s3;
            _s3 = s4;
            return result;
        }
    }

    public int Next(int maxValue)
    {
        if (maxValue < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue, "maxValue must be > 0");
        }
        return NextInner(maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue, "maxValue must be > minValue");
        }
        unchecked
        {
            long num = (long)maxValue - (long)minValue;
            if (num <= int.MaxValue)
            {
                return NextInner((int)num) + minValue;
            }
            return (int)(NextInner(num) + minValue);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double NextDouble()
    {
        return (double)(NextULongInner() >> 11) * IncrDouble;
    }

    public int NextInt()
    {
        return (int)(NextULongInner() >> 33);
    }

    public uint NextUInt()
    {
        return unchecked((uint)NextULongInner());
    }

    public ulong NextULong()
    {
        return NextULongInner();
    }

    public bool NextBool()
    {
        return (NextULongInner() & 0x8000000000000000uL) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat()
    {
        return (float)(NextULongInner() >> 40) * IncrFloat;
    }

    private int NextInner(int maxValue)
    {
        return (int)(NextDouble() * (double)maxValue);
    }

    private long NextInner(long maxValue)
    {
        return (long)(NextDouble() * (double)maxValue);
    }

    public RngSnapshot ToSnapshot(int counter) => new(counter, _s0, _s1, _s2, _s3);

    public MegaRandom DeepClone() => new(_s0, _s1, _s2, _s3);

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RngCanonicalTags.MegaRandom);
        w.WriteUInt64(_s0);
        w.WriteUInt64(_s1);
        w.WriteUInt64(_s2);
        w.WriteUInt64(_s3);
    }
}
