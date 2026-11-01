using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Potions;

public sealed class CanonicalPotion
{
    internal CanonicalPotion(string id, PotionRarity rarity, bool canBeGeneratedInCombat)
    {
        Id = id;
        Rarity = rarity;
        CanBeGeneratedInCombat = canBeGeneratedInCombat;
    }

    public string Id { get; }

    public PotionRarity Rarity { get; }

    public bool CanBeGeneratedInCombat { get; }

    public PotionModel ToMutable() => PotionRegistry.Create(Id);

    public override string ToString() => Id;
}

public sealed class PotionPoolData
{
    private readonly CanonicalPotion[] _potions;
    private readonly string? _requiredEpochId;
    private readonly (string EpochId, string[] PotionIds)[] _epochRemovals;

    internal PotionPoolData(string id, CanonicalPotion[] potions, string? requiredEpochId, (string EpochId, string[] PotionIds)[] epochRemovals)
    {
        Id = id;
        _potions = potions;
        _requiredEpochId = requiredEpochId;
        _epochRemovals = epochRemovals;
        AllPotions = Array.AsReadOnly(potions);
    }

    public string Id { get; }

    public ReadOnlyCollection<CanonicalPotion> AllPotions { get; }

    public bool Contains(string potionId)
    {
        for (int i = 0; i < _potions.Length; i++)
        {
            if (_potions[i].Id == potionId)
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<CanonicalPotion> GetUnlockedPotions(UnlockProfile unlocks)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        if (_requiredEpochId != null)
        {
            if (!unlocks.IsEpochRevealed(_requiredEpochId))
            {
                return Array.Empty<CanonicalPotion>();
            }
            return AllPotions;
        }
        if (_epochRemovals.Length == 0)
        {
            return AllPotions;
        }
        var val = new List<CanonicalPotion>(_potions);
        for (int i = 0; i < _epochRemovals.Length; i++)
        {
            var (epochId, potionIds) = _epochRemovals[i];
            if (unlocks.IsEpochRevealed(epochId))
            {
                continue;
            }
            for (int j = 0; j < potionIds.Length; j++)
            {
                val.Remove(PotionPools.Canonical(potionIds[j]));
            }
        }
        return val;
    }
}

public static class PotionPools
{
    public const string IroncladPotionPool = "IRONCLAD_POTION_POOL";
    public const string SilentPotionPool = "SILENT_POTION_POOL";
    public const string RegentPotionPool = "REGENT_POTION_POOL";
    public const string NecrobinderPotionPool = "NECROBINDER_POTION_POOL";
    public const string DefectPotionPool = "DEFECT_POTION_POOL";
    public const string DeprecatedPotionPool = "DEPRECATED_POTION_POOL";
    public const string EventPotionPool = "EVENT_POTION_POOL";
    public const string MockPotionPool = "MOCK_POTION_POOL";
    public const string SharedPotionPool = "SHARED_POTION_POOL";
    public const string TokenPotionPool = "TOKEN_POTION_POOL";

    public const string Ironclad4Epoch = "IRONCLAD4_EPOCH";
    public const string Silent4Epoch = "SILENT4_EPOCH";
    public const string Regent4Epoch = "REGENT4_EPOCH";
    public const string Necrobinder4Epoch = "NECROBINDER4_EPOCH";
    public const string Defect4Epoch = "DEFECT4_EPOCH";
    public const string Potion1Epoch = "POTION1_EPOCH";
    public const string Potion2Epoch = "POTION2_EPOCH";

    private static readonly CanonicalPotion[] Canonicals =
    {
        new("BLOOD_POTION", PotionRarity.Common, true),
        new("SOLDIERS_STEW", PotionRarity.Rare, true),
        new("ASHWATER", PotionRarity.Uncommon, true),
        new("POISON_POTION", PotionRarity.Common, true),
        new("GHOST_IN_A_JAR", PotionRarity.Rare, true),
        new("CUNNING_POTION", PotionRarity.Uncommon, true),
        new("STAR_POTION", PotionRarity.Common, true),
        new("COSMIC_CONCOCTION", PotionRarity.Rare, true),
        new("KINGS_COURAGE", PotionRarity.Uncommon, true),
        new("POTION_OF_DOOM", PotionRarity.Common, true),
        new("POT_OF_GHOULS", PotionRarity.Rare, true),
        new("BONE_BREW", PotionRarity.Uncommon, true),
        new("FOCUS_POTION", PotionRarity.Common, true),
        new("ESSENCE_OF_DARKNESS", PotionRarity.Rare, true),
        new("POTION_OF_CAPACITY", PotionRarity.Uncommon, true),
        new("DEPRECATED_POTION", PotionRarity.None, true),
        new("AMBERGRIS", PotionRarity.Event, true),
        new("FOUL_POTION", PotionRarity.Event, true),
        new("GLOWWATER_POTION", PotionRarity.Event, true),
        new("MOCK_DISCARD_AND_ADD_SHIVS_POTION", PotionRarity.None, true),
        new("ATTACK_POTION", PotionRarity.Common, true),
        new("BEETLE_JUICE", PotionRarity.Rare, true),
        new("BLESSING_OF_THE_FORGE", PotionRarity.Uncommon, true),
        new("BLOCK_POTION", PotionRarity.Common, true),
        new("BOTTLED_POTENTIAL", PotionRarity.Rare, true),
        new("CLARITY", PotionRarity.Uncommon, true),
        new("COLORLESS_POTION", PotionRarity.Common, true),
        new("CURE_ALL", PotionRarity.Uncommon, true),
        new("DEXTERITY_POTION", PotionRarity.Common, true),
        new("DISTILLED_CHAOS", PotionRarity.Rare, true),
        new("DROPLET_OF_PRECOGNITION", PotionRarity.Rare, true),
        new("DUPLICATOR", PotionRarity.Uncommon, true),
        new("ENERGY_POTION", PotionRarity.Common, true),
        new("ENTROPIC_BREW", PotionRarity.Rare, true),
        new("EXPLOSIVE_AMPOULE", PotionRarity.Common, true),
        new("FAIRY_IN_A_BOTTLE", PotionRarity.Rare, false),
        new("FIRE_POTION", PotionRarity.Common, true),
        new("FLEX_POTION", PotionRarity.Common, true),
        new("FORTIFIER", PotionRarity.Uncommon, true),
        new("FRUIT_JUICE", PotionRarity.Rare, false),
        new("FYSH_OIL", PotionRarity.Uncommon, true),
        new("GAMBLERS_BREW", PotionRarity.Uncommon, true),
        new("GIGANTIFICATION_POTION", PotionRarity.Rare, true),
        new("HEART_OF_IRON", PotionRarity.Uncommon, true),
        new("LIQUID_BRONZE", PotionRarity.Uncommon, true),
        new("LIQUID_MEMORIES", PotionRarity.Rare, true),
        new("LUCKY_TONIC", PotionRarity.Rare, true),
        new("MAZALETHS_GIFT", PotionRarity.Rare, true),
        new("OROBIC_ACID", PotionRarity.Rare, true),
        new("POTION_OF_BINDING", PotionRarity.Uncommon, true),
        new("POWDERED_DEMISE", PotionRarity.Uncommon, true),
        new("POWER_POTION", PotionRarity.Common, true),
        new("RADIANT_TINCTURE", PotionRarity.Uncommon, true),
        new("REGEN_POTION", PotionRarity.Uncommon, false),
        new("SHACKLING_POTION", PotionRarity.Rare, true),
        new("SHIP_IN_A_BOTTLE", PotionRarity.Rare, true),
        new("SKILL_POTION", PotionRarity.Common, true),
        new("SNECKO_OIL", PotionRarity.Rare, true),
        new("SPEED_POTION", PotionRarity.Common, true),
        new("STABLE_SERUM", PotionRarity.Uncommon, true),
        new("STRENGTH_POTION", PotionRarity.Common, true),
        new("SWIFT_POTION", PotionRarity.Common, true),
        new("TOUCH_OF_INSANITY", PotionRarity.Uncommon, true),
        new("VULNERABLE_POTION", PotionRarity.Common, true),
        new("WEAK_POTION", PotionRarity.Common, true),
        new("POTION_SHAPED_ROCK", PotionRarity.Token, true)
    };

    private static readonly PotionPoolData[] Pools =
    {
        Pool(IroncladPotionPool, Ironclad4Epoch, "BLOOD_POTION", "SOLDIERS_STEW", "ASHWATER"),
        Pool(SilentPotionPool, Silent4Epoch, "POISON_POTION", "GHOST_IN_A_JAR", "CUNNING_POTION"),
        Pool(RegentPotionPool, Regent4Epoch, "STAR_POTION", "COSMIC_CONCOCTION", "KINGS_COURAGE"),
        Pool(NecrobinderPotionPool, Necrobinder4Epoch, "POTION_OF_DOOM", "POT_OF_GHOULS", "BONE_BREW"),
        Pool(DefectPotionPool, Defect4Epoch, "FOCUS_POTION", "ESSENCE_OF_DARKNESS", "POTION_OF_CAPACITY"),
        Pool(DeprecatedPotionPool, null, "DEPRECATED_POTION"),
        Pool(EventPotionPool, null, "AMBERGRIS", "FOUL_POTION", "GLOWWATER_POTION"),
        Pool(MockPotionPool, null, "MOCK_DISCARD_AND_ADD_SHIVS_POTION"),
        new(SharedPotionPool, Resolve(new[]
        {
            "ATTACK_POTION", "BEETLE_JUICE", "BLESSING_OF_THE_FORGE", "BLOCK_POTION", "BOTTLED_POTENTIAL", "CLARITY",
            "COLORLESS_POTION", "CURE_ALL", "DEXTERITY_POTION", "DISTILLED_CHAOS", "DROPLET_OF_PRECOGNITION",
            "DUPLICATOR", "ENERGY_POTION", "ENTROPIC_BREW", "EXPLOSIVE_AMPOULE", "FAIRY_IN_A_BOTTLE", "FIRE_POTION",
            "FLEX_POTION", "FORTIFIER", "FRUIT_JUICE", "FYSH_OIL", "GAMBLERS_BREW", "GIGANTIFICATION_POTION",
            "HEART_OF_IRON", "LIQUID_BRONZE", "LIQUID_MEMORIES", "LUCKY_TONIC", "MAZALETHS_GIFT", "OROBIC_ACID",
            "POTION_OF_BINDING", "POWDERED_DEMISE", "POWER_POTION", "RADIANT_TINCTURE", "REGEN_POTION",
            "SHACKLING_POTION", "SHIP_IN_A_BOTTLE", "SKILL_POTION", "SNECKO_OIL", "SPEED_POTION", "STABLE_SERUM",
            "STRENGTH_POTION", "SWIFT_POTION", "TOUCH_OF_INSANITY", "VULNERABLE_POTION", "WEAK_POTION"
        }), null, new[]
        {
            (Potion1Epoch, new[] { "BEETLE_JUICE", "MAZALETHS_GIFT", "DROPLET_OF_PRECOGNITION" }),
            (Potion2Epoch, new[] { "POWDERED_DEMISE", "SHIP_IN_A_BOTTLE", "TOUCH_OF_INSANITY" })
        }),
        Pool(TokenPotionPool, null, "POTION_SHAPED_ROCK")
    };

    private static readonly (string CharacterId, string PoolId)[] CharacterPools =
    {
        ("IRONCLAD", IroncladPotionPool),
        ("SILENT", SilentPotionPool),
        ("REGENT", RegentPotionPool),
        ("NECROBINDER", NecrobinderPotionPool),
        ("DEFECT", DefectPotionPool),
        ("DEPRECATED_CHARACTER", IroncladPotionPool),
        ("DEPRIVED", IroncladPotionPool),
        ("RANDOM_CHARACTER", IroncladPotionPool)
    };

    public static readonly ReadOnlyCollection<PotionPoolData> AllPotionPools = Array.AsReadOnly(Pools);

    public static readonly ReadOnlyCollection<CanonicalPotion> AllCanonicalPotions = Array.AsReadOnly(Canonicals);

    public static CanonicalPotion Canonical(string potionId)
    {
        for (int i = 0; i < Canonicals.Length; i++)
        {
            if (Canonicals[i].Id == potionId)
            {
                return Canonicals[i];
            }
        }
        throw new KeyNotFoundException($"No canonical potion with id '{potionId}'");
    }

    public static PotionPoolData Get(string poolId)
    {
        for (int i = 0; i < Pools.Length; i++)
        {
            if (Pools[i].Id == poolId)
            {
                return Pools[i];
            }
        }
        throw new KeyNotFoundException($"No potion pool with id '{poolId}'");
    }

    public static PotionPoolData ForCharacter(string characterId)
    {
        for (int i = 0; i < CharacterPools.Length; i++)
        {
            if (CharacterPools[i].CharacterId == characterId)
            {
                return Get(CharacterPools[i].PoolId);
            }
        }
        throw new KeyNotFoundException($"No potion pool for character '{characterId}'");
    }

    public static string PoolOf(string potionId)
    {
        for (int i = 0; i < Pools.Length; i++)
        {
            if (Pools[i].Contains(potionId))
            {
                return Pools[i].Id;
            }
        }
        throw new InvalidOperationException($"Potion {potionId} is in no potion pool (Models/PotionModel.cs:448: First over ModelDb.AllPotionPools).");
    }

    private static PotionPoolData Pool(string id, string? requiredEpochId, params string[] potionIds)
        => new(id, Resolve(potionIds), requiredEpochId, Array.Empty<(string, string[])>());

    private static CanonicalPotion[] Resolve(string[] potionIds)
    {
        var result = new CanonicalPotion[potionIds.Length];
        for (int i = 0; i < potionIds.Length; i++)
        {
            result[i] = Canonical(potionIds[i]);
        }
        return result;
    }
}
