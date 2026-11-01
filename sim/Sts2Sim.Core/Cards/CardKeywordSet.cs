using System.Collections;
using System.Numerics;

namespace Sts2Sim.Core.Cards;

public sealed class CardKeywordSet : ISet<CardKeyword>, IReadOnlySet<CardKeyword>
{
    public CardKeywordSet()
    {
    }

    public CardKeywordSet(int mask)
    {
        Mask = mask;
    }

    public int Mask { get; private set; }

    public int Count => BitOperations.PopCount((uint)Mask);

    public bool IsReadOnly => false;

    public static int BitOf(CardKeyword keyword)
    {
        int value = (int)keyword;
        if ((uint)value >= 32u)
        {
            throw new ArgumentOutOfRangeException(nameof(keyword), keyword, "Card keywords are stored as bits 0..31.");
        }
        return 1 << value;
    }

    public static int MaskOf(IEnumerable<CardKeyword> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        if (keywords is CardKeywordSet set)
        {
            return set.Mask;
        }
        int mask = 0;
        foreach (var keyword in keywords)
        {
            mask |= BitOf(keyword);
        }
        return mask;
    }

    public bool Add(CardKeyword item)
    {
        int bit = BitOf(item);
        bool added = (Mask & bit) == 0;
        Mask |= bit;
        return added;
    }

    void ICollection<CardKeyword>.Add(CardKeyword item) => Add(item);

    public bool Remove(CardKeyword item)
    {
        int bit = BitOf(item);
        bool removed = (Mask & bit) != 0;
        Mask &= ~bit;
        return removed;
    }

    public void Clear() => Mask = 0;

    public bool Contains(CardKeyword item) => (uint)item < 32u && (Mask & (1 << (int)item)) != 0;

    public void CopyTo(CardKeyword[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var keyword in this)
        {
            array[arrayIndex++] = keyword;
        }
    }

    public void UnionWith(IEnumerable<CardKeyword> other) => Mask |= MaskOf(other);

    public void IntersectWith(IEnumerable<CardKeyword> other) => Mask &= MaskOf(other);

    public void ExceptWith(IEnumerable<CardKeyword> other) => Mask &= ~MaskOf(other);

    public void SymmetricExceptWith(IEnumerable<CardKeyword> other) => Mask ^= MaskOf(other);

    public bool IsSubsetOf(IEnumerable<CardKeyword> other)
    {
        int o = MaskOf(other);
        return (Mask & ~o) == 0;
    }

    public bool IsSupersetOf(IEnumerable<CardKeyword> other)
    {
        int o = MaskOf(other);
        return (o & ~Mask) == 0;
    }

    public bool IsProperSubsetOf(IEnumerable<CardKeyword> other)
    {
        int o = MaskOf(other);
        return (Mask & ~o) == 0 && Mask != o;
    }

    public bool IsProperSupersetOf(IEnumerable<CardKeyword> other)
    {
        int o = MaskOf(other);
        return (o & ~Mask) == 0 && Mask != o;
    }

    public bool Overlaps(IEnumerable<CardKeyword> other) => (Mask & MaskOf(other)) != 0;

    public bool SetEquals(IEnumerable<CardKeyword> other) => Mask == MaskOf(other);

    public IEnumerator<CardKeyword> GetEnumerator()
    {
        uint mask = (uint)Mask;
        for (int k = 0; mask != 0u; k++, mask >>= 1)
        {
            if ((mask & 1u) != 0u)
            {
                yield return (CardKeyword)k;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
