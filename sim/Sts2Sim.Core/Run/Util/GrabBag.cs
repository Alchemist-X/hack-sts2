namespace Sts2Sim.Core.Run;

public sealed class GrabBag<T>
{
    private readonly List<(T Element, double Weight)> _entries = new();

    private double _totalWeight;

    public int Count => _entries.Count;

    public void Add(T element, double weight)
    {
        _entries.Add((element, weight));
        _totalWeight += weight;
    }

    public T? Grab(Rng rng, Func<T, bool>? predicate = null)
    {
        int num = GrabIndex(rng, predicate);
        if (num < 0)
        {
            return default;
        }
        return _entries[num].Element;
    }

    public T? GrabAndRemove(Rng rng, Func<T, bool>? predicate = null)
    {
        int num = GrabIndex(rng, predicate);
        if (num < 0)
        {
            return default;
        }
        T item = _entries[num].Element;
        Remove(num);
        return item;
    }

    public bool Any() => _entries.Count > 0;

    public GrabBag<T> Clone()
    {
        var clone = new GrabBag<T>();
        clone._entries.AddRange(_entries);
        clone._totalWeight = _totalWeight;
        return clone;
    }

    private int GrabIndex(Rng rng, Func<T, bool>? predicate)
    {
        if (predicate != null && !_entries.Any(e => predicate(e.Element)))
        {
            return -1;
        }
        int num;
        do
        {
            num = GrabIndex(rng);
        }
        while (predicate != null && num >= 0 && !predicate(_entries[num].Element));
        return num;
    }

    private int GrabIndex(Rng rng)
    {
        double num = rng.NextDouble() * _totalWeight;
        double num2 = 0.0;
        for (int i = 0; i < _entries.Count; i++)
        {
            num2 += _entries[i].Weight;
            if (num < num2)
            {
                return i;
            }
        }
        return -1;
    }

    private void Remove(int index)
    {
        _totalWeight -= _entries[index].Weight;
        _entries.RemoveAt(index);
    }
}
