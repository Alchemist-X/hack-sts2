namespace Sts2Sim.Core.Random;

public sealed class RunRngSetSnapshot : ICanonical
{
    public string? Seed { get; set; }

    public ulong? HashedSeed { get; set; }

    public Dictionary<RunRngType, RngSnapshot> Rngs { get; set; } = new();

    public RunRngSetSnapshot DeepClone() => new()
    {
        Seed = Seed,
        HashedSeed = HashedSeed,
        Rngs = new Dictionary<RunRngType, RngSnapshot>(Rngs)
    };

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RngCanonicalTags.RunRngSetSnapshot);
        w.WriteString(Seed);
        w.WriteBool(HashedSeed.HasValue);
        if (HashedSeed is ulong hashedSeed)
        {
            w.WriteUInt64(hashedSeed);
        }
        RunRngType[] keys = new RunRngType[Rngs.Count];
        Rngs.Keys.CopyTo(keys, 0);
        Array.Sort(keys, static (a, b) => ((int)a).CompareTo((int)b));
        w.WriteCount(keys.Length);
        foreach (RunRngType key in keys)
        {
            w.WriteInt32((int)key);
            Rngs[key].WriteCanonical(w);
        }
    }
}

public sealed class PlayerRngSetSnapshot : ICanonical
{
    public ulong Seed { get; set; }

    public Dictionary<PlayerRngType, RngSnapshot> Rngs { get; set; } = new();

    public PlayerRngSetSnapshot DeepClone() => new()
    {
        Seed = Seed,
        Rngs = new Dictionary<PlayerRngType, RngSnapshot>(Rngs)
    };

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RngCanonicalTags.PlayerRngSetSnapshot);
        w.WriteUInt64(Seed);
        PlayerRngType[] keys = new PlayerRngType[Rngs.Count];
        Rngs.Keys.CopyTo(keys, 0);
        Array.Sort(keys, static (a, b) => ((int)a).CompareTo((int)b));
        w.WriteCount(keys.Length);
        foreach (PlayerRngType key in keys)
        {
            w.WriteInt32((int)key);
            Rngs[key].WriteCanonical(w);
        }
    }
}
