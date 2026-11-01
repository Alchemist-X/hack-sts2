namespace Sts2Sim.Core.Potions;

public static partial class PotionFactory
{
    public const float RareChance = 0.1f;

    public const float RareThreshold = 0.1f;

    public const float UncommonChance = 0.25f;

    public const float UncommonThreshold = 0.35f;

    public static CanonicalPotion CreateRandomPotionOutOfCombat(RunState rs, PlayerState player, Rng rng, IEnumerable<CanonicalPotion>? blacklist = null)
    {
        return CreateRandomPotionsOutOfCombat(rs, player, 1, rng, blacklist).First();
    }

    public static IEnumerable<CanonicalPotion> CreateRandomPotionsOutOfCombat(RunState rs, PlayerState player, int count, Rng rng, IEnumerable<CanonicalPotion>? blacklist = null)
    {
        IEnumerable<CanonicalPotion> enumerable = GetPotionOptions(rs, player);
        if (blacklist != null)
        {
            enumerable = enumerable.Except(blacklist);
        }
        return CreateRandomPotions(enumerable, count, rng);
    }

    public static CanonicalPotion CreateRandomPotionInCombat(RunState rs, PlayerState player, Rng rng, IEnumerable<CanonicalPotion>? blacklist = null)
    {
        IEnumerable<CanonicalPotion> enumerable = GetPotionOptions(rs, player).Where(static p => p.CanBeGeneratedInCombat);
        if (blacklist != null)
        {
            enumerable = enumerable.Except(blacklist);
        }
        return CreateRandomPotions(enumerable, 1, rng).First();
    }

    private static IEnumerable<CanonicalPotion> CreateRandomPotions(IEnumerable<CanonicalPotion> options, int count, Rng rng)
    {
        var val = options.ToList();
        var val2 = new List<CanonicalPotion>();
        for (int i = 0; i < count; i++)
        {
            float num = rng.NextFloat();
            var rarity = num <= RareThreshold ? PotionRarity.Rare : (!(num <= UncommonThreshold) ? PotionRarity.Common : PotionRarity.Uncommon);
            var potion = rng.NextItem(val.Where(p => p.Rarity == rarity));
            val2.Add(potion!);
            val.Remove(potion!);
        }
        return val2;
    }

    public static IEnumerable<CanonicalPotion> GetPotionOptions(RunState rs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        return PotionPools.ForCharacter(player.CharacterId).GetUnlockedPotions(rs.Unlocks)
            .Concat(PotionPools.Get(PotionPools.SharedPotionPool).GetUnlockedPotions(rs.Unlocks));
    }
}
