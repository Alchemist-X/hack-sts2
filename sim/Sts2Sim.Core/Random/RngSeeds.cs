namespace Sts2Sim.Core.Random;

public static class RngSeeds
{
    public const string OldSeedPrefix = "old";

    public const string SeedCharacters = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public const int SeedDefaultLength = 12;

    public static ulong RunSeed(string seed)
    {
        if (seed.StartsWith(OldSeedPrefix, StringComparison.Ordinal))
        {
            int length = OldSeedPrefix.Length;
            return unchecked((uint)DeterministicHash.GetDeterministicHashCodeOld(seed.Substring(length, seed.Length - length)));
        }
        return DeterministicHash.GetDeterministicHashCode(seed);
    }

    public static ulong Named(ulong seed, string name)
    {
        return unchecked(seed + DeterministicHash.GetDeterministicHashCode(name));
    }

    public static ulong PlayerModel(ulong runSeed, int playerSlotIndex, string modelIdEntry, ulong mixin = 0uL)
    {
        unchecked
        {
            return (ulong)((long)runSeed + (long)playerSlotIndex + (long)DeterministicHash.GetDeterministicHashCode(modelIdEntry)) + mixin;
        }
    }

    public static ulong PlayerSet(string runStringSeed, int playerSlotIndex)
    {
        unchecked
        {
            return DeterministicHash.GetDeterministicHashCode(runStringSeed) + (ulong)playerSlotIndex;
        }
    }

    public static ulong PlayerSetFromHashedRunSeed(ulong hashedRunSeed, int playerSlotIndex)
    {
        unchecked
        {
            return hashedRunSeed + (ulong)playerSlotIndex;
        }
    }

    public static ulong Monster(ulong runSeed, int mapCol, int mapRow, int actIndex, uint combatId)
    {
        unchecked
        {
            return (ulong)((long)runSeed + (long)mapCol + mapRow + actIndex + combatId);
        }
    }

    public static ulong Encounter(ulong runSeed, int totalFloor, string encounterIdEntry)
    {
        unchecked
        {
            return (ulong)((long)runSeed + (long)totalFloor) + DeterministicHash.GetDeterministicHashCode(encounterIdEntry);
        }
    }

    public static ulong Event(ulong runSeed, bool isShared, int playerSlotIndex, string eventIdEntry)
    {
        unchecked
        {
            return (ulong)((long)runSeed + (long)(isShared ? 0 : playerSlotIndex)) + DeterministicHash.GetDeterministicHashCode(eventIdEntry);
        }
    }

    public static MegaRandom LegacyV18Kernel(ulong seed, string type, int counter)
    {
        uint num = unchecked((uint)((int)seed + DeterministicHash.GetDeterministicHashCodeOld(type)));
        MegaRandom megaRandom = new MegaRandom(num);
        for (int i = 0; i < counter; i++)
        {
            megaRandom.NextULong();
        }
        return megaRandom;
    }

    public static string CanonicalizeSeed(string seed)
    {
        seed = seed.ToUpperInvariant();
        seed = seed.Replace('O', '0');
        seed = seed.Replace('I', '1');
        seed = seed.Trim();
        return seed;
    }
}
