using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Sts2Sim.Core.Random;

public static class DeterministicHash
{
    private const ulong Prime64_1 = 0x9E3779B185EBCA87uL;

    private const ulong Prime64_2 = 0xC2B2AE3D27D4EB4FuL;

    private const ulong Prime64_3 = 0x165667B19E3779F9uL;

    private const ulong Prime64_4 = 0x85EBCA77C2B2AE63uL;

    private const ulong Prime64_5 = 0x27D4EB2F165667C5uL;

    private const int StackBufferBytes = 256;

    private static readonly Regex CamelCaseRegex = new("([A-Za-z0-9]|\\G(?!^))([A-Z])", RegexOptions.None);

    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.None);

    private static readonly Regex SpecialCharRegex = new("[^A-Z0-9_]", RegexOptions.None);

    public static ulong GetDeterministicHashCode(string str)
    {
        int byteCount = Encoding.UTF8.GetByteCount(str);
        Span<byte> buffer = byteCount <= StackBufferBytes ? stackalloc byte[StackBufferBytes] : new byte[byteCount];
        int written = Encoding.UTF8.GetBytes(str.AsSpan(), buffer);
        return XxHash64(buffer.Slice(0, written), 0L);
    }

    public static int GetDeterministicHashCodeOld(string str)
    {
        unchecked
        {
            int num = 352654597;
            int num2 = num;
            for (int i = 0; i < str.Length; i += 2)
            {
                num = ((num << 5) + num) ^ str[i];
                if (i == str.Length - 1)
                {
                    break;
                }
                num2 = ((num2 << 5) + num2) ^ str[i + 1];
            }
            return num + num2 * 1566083941;
        }
    }

    public static ulong XxHash64(ReadOnlySpan<byte> data, long seed = 0L)
    {
        unchecked
        {
            ulong s = (ulong)seed;
            int length = data.Length;
            int p = 0;
            ulong h;
            if (length >= 32)
            {
                ulong v1 = s + Prime64_1 + Prime64_2;
                ulong v2 = s + Prime64_2;
                ulong v3 = s;
                ulong v4 = s - Prime64_1;
                int limit = length - 32;
                do
                {
                    v1 = XxRound(v1, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(p, 8)));
                    v2 = XxRound(v2, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(p + 8, 8)));
                    v3 = XxRound(v3, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(p + 16, 8)));
                    v4 = XxRound(v4, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(p + 24, 8)));
                    p += 32;
                }
                while (p <= limit);
                h = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7)
                    + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
                h = XxMergeRound(h, v1);
                h = XxMergeRound(h, v2);
                h = XxMergeRound(h, v3);
                h = XxMergeRound(h, v4);
            }
            else
            {
                h = s + Prime64_5;
            }
            h += (ulong)length;
            while (p + 8 <= length)
            {
                h ^= XxRound(0uL, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(p, 8)));
                h = BitOperations.RotateLeft(h, 27) * Prime64_1 + Prime64_4;
                p += 8;
            }
            if (p + 4 <= length)
            {
                h ^= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4)) * Prime64_1;
                h = BitOperations.RotateLeft(h, 23) * Prime64_2 + Prime64_3;
                p += 4;
            }
            while (p < length)
            {
                h ^= (ulong)data[p] * Prime64_5;
                h = BitOperations.RotateLeft(h, 11) * Prime64_1;
                p++;
            }
            h ^= h >> 33;
            h *= Prime64_2;
            h ^= h >> 29;
            h *= Prime64_3;
            h ^= h >> 32;
            return h;
        }
    }

    public static string SnakeCase(string txt)
    {
        return CamelCaseRegex.Replace(txt.Trim(), "$1_$2").ToLowerInvariant();
    }

    public static string Slugify(string txt)
    {
        string text = CamelCaseRegex.Replace(txt.Trim(), "$1_$2");
        string text2 = WhitespaceRegex.Replace(text.ToUpperInvariant(), "_");
        return SpecialCharRegex.Replace(text2, "");
    }

    private static ulong XxRound(ulong acc, ulong input)
    {
        unchecked
        {
            acc += input * Prime64_2;
            acc = BitOperations.RotateLeft(acc, 31);
            return acc * Prime64_1;
        }
    }

    private static ulong XxMergeRound(ulong acc, ulong val)
    {
        unchecked
        {
            acc ^= XxRound(0uL, val);
            return acc * Prime64_1 + Prime64_4;
        }
    }
}
