namespace Sts2Sim.Core.Run;

public sealed class GrabBag<T>
{
    public void Add(T element, double weight) => throw new NotImplementedException("W2 R3: GrabBag.Add");

    public T? Grab(Rng rng, Func<T, bool>? predicate = null) => throw new NotImplementedException("W2 R3: GrabBag.Grab");

    public T? GrabAndRemove(Rng rng, Func<T, bool>? predicate = null) => throw new NotImplementedException("W2 R3: GrabBag.GrabAndRemove");

    public bool Any() => throw new NotImplementedException("W2 R3: GrabBag.Any");
}
