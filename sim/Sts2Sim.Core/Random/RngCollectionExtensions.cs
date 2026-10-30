namespace Sts2Sim.Core.Random;

public static class RngCollectionExtensions
{
    public static List<T> StableShuffle<T>(this List<T> list, Rng rng) where T : IComparable<T>
    {
        list.Sort();
        return UnstableShuffle(list, rng);
    }

    public static List<T> StableShuffle<T>(this List<T> list, Rng rng, IComparer<T> comparer)
    {
        list.Sort(comparer);
        return UnstableShuffle(list, rng);
    }

    public static List<T> StableShuffle<T>(this List<T> list, Rng rng, Comparison<T> comparison)
    {
        list.Sort(comparison);
        return UnstableShuffle(list, rng);
    }

    public static List<T> UnstableShuffle<T>(this List<T> list, Rng rng)
    {
        int num = list.Count;
        while (num > 1)
        {
            num--;
            int num2 = rng.NextInt(num + 1);
            int num3 = num2;
            int num4 = num;
            T val = list[num];
            T val2 = list[num2];
            list[num3] = val;
            list[num4] = val2;
        }
        return list;
    }

    public static IEnumerable<T> TakeRandom<T>(this IEnumerable<T> collection, int count, Rng rng)
    {
        return UnstableShuffle(collection.ToList(), rng).Take(count);
    }
}
