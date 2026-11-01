namespace Sts2Sim.Core.Run;

public sealed class RelicGrabBag : ICanonical
{
    public RelicGrabBag() : this(false)
    {
    }

    public RelicGrabBag(bool refreshAllowed)
    {
        RefreshAllowed = refreshAllowed;
    }

    public bool RefreshAllowed { get; }

    public bool IsPopulated => throw new NotImplementedException("W2 R3: RelicGrabBag.IsPopulated");

    public bool HasAvailableRelics(RunState runState) => throw new NotImplementedException("W2 R3: RelicGrabBag.HasAvailableRelics");

    public void Populate(RunState rs, PlayerState player, Rng rng) => throw new NotImplementedException("W2 R3: RelicGrabBag.Populate(player)");

    public void Populate(IEnumerable<string> relicIds, Rng rng) => throw new NotImplementedException("W2 R3: RelicGrabBag.Populate(relics)");

    public string? PullFromFront(RelicRarity rarity, RunState runState) => throw new NotImplementedException("W2 R3: RelicGrabBag.PullFromFront");

    public string? PullFromFront(RelicRarity rarity, Func<string, bool> filter, RunState runState) => throw new NotImplementedException("W2 R3: RelicGrabBag.PullFromFront");

    public string? PullFromBack(RelicRarity rarity, Func<string, bool> filter, RunState runState) => throw new NotImplementedException("W2 R3: RelicGrabBag.PullFromBack");

    public void Remove(string relicId) => throw new NotImplementedException("W2 R3: RelicGrabBag.Remove");

    public bool Contains(string relicId) => throw new NotImplementedException("W2 R3: RelicGrabBag.Contains");

    public RelicGrabBag DeepClone() => throw new NotImplementedException("W2 R3: RelicGrabBag.DeepClone");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: RelicGrabBag.WriteCanonical");
}
