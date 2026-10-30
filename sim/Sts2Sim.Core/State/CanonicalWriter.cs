using System.Buffers.Binary;
using System.Text;

namespace Sts2Sim.Core.State;

public interface ICanonical
{
    void WriteCanonical(CanonicalWriter w);
}

public sealed class CanonicalWriter
{
    public const int NullStringLength = -1;

    private const int MinimumCapacity = 16;

    private byte[] _buffer;
    private int _length;

    public CanonicalWriter(int initialCapacity = 4096)
    {
        _buffer = new byte[Math.Max(MinimumCapacity, initialCapacity)];
    }

    public int Length => _length;

    public int Capacity => _buffer.Length;

    public ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, _length);

    public void Reset() => _length = 0;

    public byte[] ToArray() => WrittenSpan.ToArray();

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);

    public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);

    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);

    public void WriteSingle(float value) => WriteUInt32(BitConverter.SingleToUInt32Bits(value));

    public void WriteDouble(double value) => WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

    public void WriteDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        uint lo = unchecked((uint)bits[0]);
        uint mid = unchecked((uint)bits[1]);
        uint hi = unchecked((uint)bits[2]);
        int flags = bits[3];
        int scale = (flags >> 16) & 0xFF;
        bool negative = flags < 0;
        if ((lo | mid | hi) == 0)
        {
            scale = 0;
            negative = false;
        }
        else
        {
            while (scale > 0)
            {
                uint qHi = hi;
                uint qMid = mid;
                uint qLo = lo;
                if (DivRem10(ref qHi, ref qMid, ref qLo) != 0)
                {
                    break;
                }
                hi = qHi;
                mid = qMid;
                lo = qLo;
                scale--;
            }
        }
        WriteByte((byte)scale);
        WriteBool(negative);
        WriteUInt32(lo);
        WriteUInt32(mid);
        WriteUInt32(hi);
    }

    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(NullStringLength);
            return;
        }
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(byteCount);
        if (byteCount > 0)
        {
            Encoding.UTF8.GetBytes(value.AsSpan(), Reserve(byteCount));
        }
    }

    public void WriteTag(ushort tag) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), tag);

    public void WriteCount(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative");
        }
        WriteInt32(count);
    }

    public void WriteNullableInt32(int? value)
    {
        WriteBool(value.HasValue);
        if (value.HasValue)
        {
            WriteInt32(value.GetValueOrDefault());
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
        {
            bytes.CopyTo(Reserve(bytes.Length));
        }
    }

    private Span<byte> Reserve(int count)
    {
        EnsureCapacity(count);
        var span = new Span<byte>(_buffer, _length, count);
        _length += count;
        return span;
    }

    private void EnsureCapacity(int extra)
    {
        long required = (long)_length + extra;
        if (required > _buffer.Length)
        {
            Grow(required);
        }
    }

    private void Grow(long required)
    {
        if (required > Array.MaxLength)
        {
            throw new InvalidOperationException("Canonical state exceeds the maximum array length");
        }
        long doubled = (long)_buffer.Length * 2;
        long newSize = Math.Min(Math.Max(required, doubled), Array.MaxLength);
        Array.Resize(ref _buffer, (int)newSize);
    }

    private static uint DivRem10(ref uint hi, ref uint mid, ref uint lo)
    {
        ulong r = hi;
        hi = (uint)(r / 10);
        r %= 10;
        r = (r << 32) | mid;
        mid = (uint)(r / 10);
        r %= 10;
        r = (r << 32) | lo;
        lo = (uint)(r / 10);
        r %= 10;
        return (uint)r;
    }
}
