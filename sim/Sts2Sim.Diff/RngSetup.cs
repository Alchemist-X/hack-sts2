namespace Sts2Sim.Diff;

public static class RngSetup
{
    public const string SeedingXxh64 = "xxh64";

    public const string SeedingLegacyV18 = "legacy_v18";

    public static bool HasOldPrefix(string seed) => seed.StartsWith(RngSeeds.OldSeedPrefix, StringComparison.Ordinal);

    public static RunRngSet Create(string seeding, string seed)
    {
        ArgumentNullException.ThrowIfNull(seeding);
        ArgumentNullException.ThrowIfNull(seed);
        if (HasOldPrefix(seed))
        {
            throw new ArgumentException($"Seed '{seed}' carries the '{RngSeeds.OldSeedPrefix}' prefix of a cross-version continuation; its streams are not derivable.", nameof(seed));
        }
        return seeding switch
        {
            SeedingXxh64 => new RunRngSet(seed),
            SeedingLegacyV18 => CreateLegacyV18(seed),
            _ => throw new ArgumentException($"Unknown seeding '{seeding}'.", nameof(seeding))
        };
    }

    public static ulong LegacyV18RunSeed(string seed) => unchecked((ulong)(long)DeterministicHash.GetDeterministicHashCodeOld(seed));

    public static RunRngSet CreateLegacyV18(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ulong legacySeed = LegacyV18RunSeed(seed);
        string migratedSeed = RngSeeds.OldSeedPrefix + seed;
        var snapshot = new RunRngSetSnapshot
        {
            Seed = migratedSeed,
            HashedSeed = RngSeeds.RunSeed(migratedSeed)
        };
        foreach (RunRngType type in Enum.GetValues<RunRngType>())
        {
            var kernel = RngSeeds.LegacyV18Kernel(legacySeed, RunRngSet.StreamName(type), 0);
            snapshot.Rngs[type] = kernel.ToSnapshot(0);
        }
        return RunRngSet.FromSnapshot(snapshot);
    }

    public static bool TryParseStream(string name, out RunRngType type)
    {
        foreach (RunRngType candidate in Enum.GetValues<RunRngType>())
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal)
                || string.Equals(RunRngSet.StreamName(candidate), name, StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }
        type = default;
        return false;
    }

    public static void ApplyOffsets(RunRngSet rng, IEnumerable<KeyValuePair<string, int>> offsets)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(offsets);
        foreach (var (name, count) in offsets)
        {
            if (!TryParseStream(name, out var type))
            {
                throw new ArgumentException($"Unknown RNG stream '{name}'.", nameof(offsets));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offsets), count, $"Offset for stream '{name}' must be non-negative.");
            }
            Advance(rng.Get(type), count);
        }
    }

    public static void Advance(Rng stream, int pulls)
    {
        for (int i = 0; i < pulls; i++)
        {
            stream.NextUnsignedLong();
        }
    }

    public static RunRngSet CreateForBundle(DiffBundle bundle)
    {
        var rng = Create(bundle.Seeding, bundle.Seed);
        ApplyOffsets(rng, bundle.RngOffsets);
        return rng;
    }
}
