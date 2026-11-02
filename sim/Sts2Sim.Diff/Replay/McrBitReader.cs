using System.Text;

namespace Sts2Sim.Diff.Replay;

public sealed class McrFormatException : Exception
{
    public McrFormatException(string message) : base(message)
    {
    }
}

public sealed class McrBitReader
{
    private readonly byte[] _buffer;

    public McrBitReader(byte[] buffer)
    {
        _buffer = buffer;
    }

    public int BitPosition { get; private set; }

    public int BitLength => _buffer.Length * 8;

    public int RemainingBits => BitLength - BitPosition;

    public McrModelNetIds? Ids { get; set; }

    public bool Legacy => Ids?.Legacy ?? false;

    private ulong ReadRaw(int bits)
    {
        if (bits < 0 || bits > 64)
        {
            throw new McrFormatException($"bit count {bits} out of range");
        }
        if (BitPosition + bits > BitLength)
        {
            throw new McrFormatException($"read of {bits} bits at bit {BitPosition} runs past the end ({BitLength} bits)");
        }
        ulong value = 0;
        int pos = BitPosition;
        int done = 0;
        while (done < bits)
        {
            int byteIndex = pos >> 3;
            int bitInByte = pos & 7;
            int take = Math.Min(8 - bitInByte, bits - done);
            ulong chunk = (ulong)((_buffer[byteIndex] >> bitInByte) & ((1 << take) - 1));
            value |= chunk << done;
            done += take;
            pos += take;
        }
        BitPosition = pos;
        return value;
    }

    public bool ReadBool() => ReadRaw(1) != 0;

    public byte ReadByte(int bits = 8) => (byte)ReadRaw(bits);

    public int ReadInt(int bits = 32) => unchecked((int)(uint)ReadRaw(bits));

    public uint ReadUInt(int bits = 32) => (uint)ReadRaw(bits);

    public long ReadLong(int bits = 64) => unchecked((long)ReadRaw(bits));

    public ulong ReadULong(int bits = 64) => ReadRaw(bits);

    public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)(uint)ReadRaw(32)));

    public float ReadQuantizedFloat(float min, float max, int bits)
    {
        uint value = ReadUInt(bits);
        return (float)(value / Math.Pow(2.0, bits) * (max - min) + min);
    }

    public static int EnumBits(int maxValue) => (int)Math.Ceiling(Math.Log2(maxValue) + 1.0);

    public int ReadEnum(int maxValue) => ReadInt(EnumBits(maxValue));

    public string ReadString()
    {
        int length = ReadInt();
        if (length < 0 || (long)length * 8 > RemainingBits)
        {
            throw new McrFormatException($"string length {length} at bit {BitPosition - 32} is out of range");
        }
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)ReadRaw(8);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public int ReadCount(int bits = 32)
    {
        int n = ReadInt(bits);
        if (n < 0 || n > RemainingBits)
        {
            throw new McrFormatException($"list count {n} at bit {BitPosition - bits} is out of range");
        }
        return n;
    }

    public List<T> ReadList<T>(Func<McrBitReader, T> item, int lengthBits = 32)
    {
        int n = ReadCount(lengthBits);
        var list = new List<T>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(item(this));
        }
        return list;
    }

    private McrModelNetIds RequireIds() => Ids ?? throw new McrFormatException("model net-id table not set");

    public string? ReadModelEntry()
    {
        var ids = RequireIds();
        string entry = ids.Entry(ReadInt(ids.EntryIdBitSize));
        return entry == McrModelNetIds.NoneEntry ? null : entry;
    }

    public string? ReadFullModelId()
    {
        var ids = RequireIds();
        string category = ids.Category(ReadInt(ids.CategoryIdBitSize));
        string entry = ids.Entry(ReadInt(ids.EntryIdBitSize));
        if (entry == McrModelNetIds.NoneEntry || category == McrModelNetIds.NoneEntry)
        {
            return null;
        }
        return category + "." + entry;
    }

    public List<string?> ReadModelEntryList()
    {
        int n = ReadCount();
        var list = new List<string?>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(ReadModelEntry());
        }
        return list;
    }

    public List<string?> ReadFullModelIdList()
    {
        int n = ReadCount();
        var list = new List<string?>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(ReadFullModelId());
        }
        return list;
    }

    public string ReadEpochId()
    {
        var ids = RequireIds();
        return ids.Epoch(ReadInt(ids.EpochIdBitSize));
    }

    public string ReadPropertyName()
    {
        var ids = RequireIds();
        return ids.Property(ReadInt(ids.PropertyIdBitSize));
    }
}
