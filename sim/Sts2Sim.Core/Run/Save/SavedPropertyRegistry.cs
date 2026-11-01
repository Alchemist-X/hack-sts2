using System.Reflection;

namespace Sts2Sim.Core.Run;

/// <summary>Saves.Runs/SerializationCondition.cs (the conditions content fields use).</summary>
public enum SavedPropertyCondition
{
    AlwaysSave,
    SaveIfNotTypeDefault
}

/// <param name="IsModelId">The game member is a <c>ModelId</c> (saved under <c>model_ids</c>); the sim holds the bare id string.</param>
public sealed record SavedPropertySpec(string Name, SavedPropertyCondition Condition = SavedPropertyCondition.AlwaysSave, int Order = 0, bool IsModelId = false);

/// <summary>
/// The game's <c>[SavedProperty]</c> members of content models (v0.111.0: 40 relic classes, 6 card classes and
/// <c>RelicModel.IsWax/IsMelted</c>), keyed by model id, so content defs need not carry <see cref="SavedPropertyAttribute"/>.
/// A spec names the sim property that holds the field (same name; on a card it lives on <c>CardInstance.Extra</c>).
/// Serialization follows Saves.Runs/SavedProperties.cs:63-200 (<c>SavedProperties.FromInternal</c>): members sorted by
/// (order, ordinal name) (Multiplayer.Serialization/ModelIdSerializationCache.cs:250-259), the condition checked per
/// value (Saves.Runs/SerializationConditionExtensions.cs:78-98), and <c>null</c> when nothing is saved.
/// Card-valued members (PAELS_TOOTH.SerializableCards, ARCHAIC_TOOTH.StarterCard/AncientCard) are listed but have no
/// sim representation, so a save holding them stays unsupported.
/// </summary>
public static class SavedPropertyRegistry
{
    public static readonly IReadOnlyList<SavedPropertySpec> RelicBase = new[]
    {
        // Models/RelicModel.cs:252-277
        new SavedPropertySpec("IsMelted", SavedPropertyCondition.SaveIfNotTypeDefault),
        new SavedPropertySpec("IsWax", SavedPropertyCondition.SaveIfNotTypeDefault)
    };

    /// <summary>Models.Relics/*.cs <c>[SavedProperty]</c> members, by relic id.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<SavedPropertySpec>> Relics = new Dictionary<string, IReadOnlyList<SavedPropertySpec>>(StringComparer.Ordinal)
    {
        ["ARCHAIC_TOOTH"] = Specs("AncientCard", "StarterCard"),
        ["BONE_TEA"] = Specs("CombatsLeft"),
        ["BOOK_OF_FIVE_RINGS"] = Specs("CardsAdded"),
        ["BYRDPIP"] = Specs("Skin"),
        ["DUSTY_TOME"] = new[] { new SavedPropertySpec("AncientCard", IsModelId: true) },
        ["EMBER_TEA"] = Specs("CombatsLeft"),
        ["FAKE_HAPPY_FLOWER"] = Specs("TurnsSeen"),
        ["FAKE_VENERABLE_TEA_SET"] = Specs("GainEnergyInNextCombat"),
        ["FISHING_ROD"] = Specs("CombatsSeen"),
        ["FUR_COAT"] = Specs("FurCoatActIndex", "FurCoatCoordCols", "FurCoatCoordRows", "FurCoatCoordsSet"),
        ["GALACTIC_DUST"] = Specs("StarsSpent"),
        ["GIRYA"] = Specs("TimesLifted"),
        ["GOLDEN_COMPASS"] = Specs("GoldenPathAct"),
        ["HAPPY_FLOWER"] = Specs("TurnsSeen"),
        ["IRON_CLUB"] = Specs("CardsPlayed"),
        ["JOSS_PAPER"] = new[] { new SavedPropertySpec("CardsExhausted", SavedPropertyCondition.SaveIfNotTypeDefault) },
        ["LASTING_CANDY"] = Specs("CombatRewardsSeen"),
        ["LAVA_LAMP"] = Specs("TookDamageThisCombat"),
        ["LAVA_ROCK"] = Specs("HasTriggered"),
        ["LIZARD_TAIL"] = Specs("WasUsed"),
        ["MAW_BANK"] = Specs("HasItemBeenBought"),
        ["NUNCHAKU"] = Specs("AttacksPlayed"),
        ["PAELS_LEGION"] = Specs("Skin"),
        ["PAELS_TOOTH"] = Specs("SerializableCards"),
        ["PAELS_WING"] = Specs("RewardsSacrificed"),
        ["PENDULUM"] = Specs("TurnsSeen"),
        ["PEN_NIB"] = Specs("AttacksPlayed"),
        ["POLLINOUS_CORE"] = Specs("TurnsSeen"),
        ["PUMPKIN_CANDLE"] = Specs("KindleCount"),
        ["SEA_GLASS"] = new[] { new SavedPropertySpec("CharacterId", IsModelId: true) },
        ["SILKEN_TRESS"] = Specs("IsUsed"),
        ["SILVER_CRUCIBLE"] = Specs("TimesUsed", "TreasureRoomsEntered"),
        ["SWORD_OF_STONE"] = Specs("ElitesDefeated"),
        ["TEA_OF_DISCOURTESY"] = Specs("CombatsLeft"),
        ["TOUCH_OF_OROBAS"] = new[] { new SavedPropertySpec("StarterRelic", IsModelId: true), new SavedPropertySpec("UpgradedRelic", IsModelId: true) },
        ["TOY_BOX"] = Specs("CombatsSeen"),
        ["TUNING_FORK"] = Specs("SkillsPlayed"),
        ["VENERABLE_TEA_SET"] = Specs("GainEnergyInNextCombat"),
        ["WINGED_BOOTS"] = Specs("TimesUsed"),
        ["WONGOS_MYSTERY_TICKET"] = Specs("CombatsFinished", "GaveRelic")
    };

    /// <summary>Models.Cards/*.cs <c>[SavedProperty]</c> members, by card id (held by the card's extra state).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<SavedPropertySpec>> Cards = new Dictionary<string, IReadOnlyList<SavedPropertySpec>>(StringComparer.Ordinal)
    {
        ["DOWSING"] = Specs("RoomsEntered"),
        ["GENETIC_ALGORITHM"] = Specs("CurrentBlock", "IncreasedBlock"),
        ["GUILTY"] = Specs("CombatsSeen"),
        ["MAD_SCIENCE"] = new[]
        {
            new SavedPropertySpec("TinkerTimeType", SavedPropertyCondition.AlwaysSave, -1),
            new SavedPropertySpec("TinkerTimeRider", SavedPropertyCondition.SaveIfNotTypeDefault)
        },
        ["SPOILS_MAP"] = Specs("SpoilsActIndex"),
        ["THE_SCYTHE"] = Specs("CurrentDamage", "IncreasedDamage")
    };

    private static readonly Lazy<Dictionary<Type, IReadOnlyList<SavedPropertySpec>>> ByType = new(BuildByType);

    private static SavedPropertySpec[] Specs(params string[] names) => names.Select(n => new SavedPropertySpec(n)).ToArray();

    /// <summary>The registered specs of a sim model object (relic, card extra state or enchantment), base specs included.</summary>
    public static IReadOnlyList<SavedPropertySpec> For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var list = new List<SavedPropertySpec>();
        if (ByType.Value.TryGetValue(type, out var own))
        {
            list.AddRange(own);
        }
        if (typeof(RelicModel).IsAssignableFrom(type))
        {
            list.AddRange(RelicBase);
        }
        return list;
    }

    public static bool IsRegistered(Type type, string name) => For(type).Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    /// <summary>The sim property backing <paramref name="name"/> on <paramref name="type"/>, or null.</summary>
    public static PropertyInfo? Member(Type type, string name)
        => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>The (spec, property) pairs in the game's save order; specs without a sim property are skipped.</summary>
    public static IEnumerable<(SavedPropertySpec Spec, PropertyInfo Member)> Ordered(Type type)
        => For(type)
            .OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => (s, Member(type, s.Name)))
            .Where(x => x.Item2 != null && x.Item2.CanRead)
            .Select(x => (x.s, x.Item2!));

    public static bool ShouldSave(SavedPropertySpec spec, object? value) => spec.Condition switch
    {
        SavedPropertyCondition.SaveIfNotTypeDefault => value switch
        {
            null => false,
            int i => i != 0,
            bool b => b,
            Enum e => Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture) != 0,
            _ => true
        },
        _ => true
    };

    private static Dictionary<Type, IReadOnlyList<SavedPropertySpec>> BuildByType()
    {
        var map = new Dictionary<Type, IReadOnlyList<SavedPropertySpec>>();
        foreach (var (id, specs) in Relics)
        {
            if (RelicRegistry.Models.Contains(id))
            {
                map[RelicRegistry.TypeOf(id)] = specs;
            }
        }
        foreach (var (id, specs) in Cards)
        {
            if (CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef && new CardInstance(def, 0).Extra is { } extra)
            {
                map[extra.GetType()] = specs;
            }
        }
        return map;
    }
}
