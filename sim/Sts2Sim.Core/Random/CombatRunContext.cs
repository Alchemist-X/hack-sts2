namespace Sts2Sim.Core.Random;

public enum RngSeedingScheme : byte
{
    Xxh64 = 0,
    LegacyV18 = 1
}

public readonly record struct CombatRunContext(int ActIndex, int TotalFloor, int? MapCol = null, int? MapRow = null, RngSeedingScheme Seeding = RngSeedingScheme.Xxh64)
{
    public const int DefaultActIndex = 0;

    public const int DefaultTotalFloor = 2;

    public const string SeedingXxh64Name = "xxh64";

    public const string SeedingLegacyV18Name = "legacy_v18";

    public static CombatRunContext Default => new(DefaultActIndex, DefaultTotalFloor);

    public static CombatRunContext ForAct(int actNumber, int totalFloor, int? mapCol = null, int? mapRow = null, RngSeedingScheme seeding = RngSeedingScheme.Xxh64)
    {
        if (actNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(actNumber), actNumber, "Act numbers start at 1.");
        }
        if (totalFloor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalFloor), totalFloor, "The total floor cannot be negative.");
        }
        return new CombatRunContext(actNumber - 1, totalFloor, mapCol, mapRow, seeding);
    }

    public static RngSeedingScheme ParseSeeding(string seeding)
    {
        ArgumentNullException.ThrowIfNull(seeding);
        return seeding switch
        {
            SeedingXxh64Name => RngSeedingScheme.Xxh64,
            SeedingLegacyV18Name => RngSeedingScheme.LegacyV18,
            _ => throw new ArgumentException($"Unknown seeding scheme '{seeding}'.", nameof(seeding))
        };
    }

    public ulong EncounterSeed(ulong runSeed, string encounterIdEntry)
    {
        ArgumentNullException.ThrowIfNull(encounterIdEntry);
        return Seeding switch
        {
            RngSeedingScheme.Xxh64 => RngSeeds.Encounter(runSeed, TotalFloor, encounterIdEntry),
            RngSeedingScheme.LegacyV18 => RngSeeds.LegacyV18Encounter(runSeed, TotalFloor, encounterIdEntry),
            _ => throw new InvalidOperationException($"Unknown seeding scheme {Seeding}.")
        };
    }

    public ulong MonsterSeed(ulong runSeed, int combatId)
    {
        return Seeding switch
        {
            RngSeedingScheme.Xxh64 => RngSeeds.Monster(runSeed, MapCol ?? 0, MapRow ?? 0, ActIndex, unchecked((uint)combatId)),
            RngSeedingScheme.LegacyV18 => throw new NotSupportedException("The v0.107.1 per-monster Rng seed is not ported; no monster rule reads that Rng."),
            _ => throw new InvalidOperationException($"Unknown seeding scheme {Seeding}.")
        };
    }

    public Rng CreateEncounterRng(ulong runSeed, string encounterIdEntry) => new(EncounterSeed(runSeed, encounterIdEntry));

    public Rng CreateMonsterRng(ulong runSeed, int combatId) => new(MonsterSeed(runSeed, combatId));
}
