using System.Collections.Frozen;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// One id vocabulary: index 0 = "" (none / padding), 1 = "?" (unknown), real ids from 2, sorted ordinal.
/// <see cref="Index"/> maps unknown ids to 1 and null/"" to 0.
/// </summary>
public sealed class VocabList
{
    public const int None = 0;
    public const int Unknown = 1;

    private readonly FrozenDictionary<string, int> _index;

    public VocabList(string kind, IEnumerable<string> ids)
    {
        Kind = kind;
        var sorted = ids.Where(s => !string.IsNullOrEmpty(s) && s != "?")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal);
        Names = new[] { "", "?" }.Concat(sorted).ToArray();
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Names.Count; i++)
        {
            d[Names[i]] = i;
        }
        _index = d.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string Kind { get; }

    public IReadOnlyList<string> Names { get; }

    public int Count => Names.Count;

    public int Index(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return None;
        }
        return _index.TryGetValue(id, out int i) ? i : Unknown;
    }

    public string NameOf(int index) => (uint)index < (uint)Names.Count ? Names[index] : "?";
}

/// <summary>
/// Vocabularies and enum lists published in the schema (CONTRACTS.md §D3.2). Built once from the Core registries.
/// CLR enum lists are indexed by the enum's underlying value (index == value). String-keyed lists
/// (RestOption, CardRewardAlt, ShopCategory, PromptItemKind) follow the vocabulary convention ("" at 0, "?" at 1).
/// </summary>
public static class Vocab
{
    /// <summary>Rest-site option ids (RestSiteOption.OptionId).</summary>
    public static readonly IReadOnlyList<string> RestOptionIds = new[] { "HEAL", "SMITH", "DIG", "COOK", "HATCH", "LIFT", "KINDLE", "CLONE", "MEND" };

    /// <summary>Card-reward alternative ids (CardRewardAlternative.OptionId).</summary>
    public static readonly IReadOnlyList<string> CardRewardAltIds = new[] { "Skip", "REROLL", "SACRIFICE" };

    /// <summary>Shop entry categories (as Sts2Sim.Diff RunScreenProjector.EntryCategory names them).</summary>
    public static readonly IReadOnlyList<string> ShopCategoryIds = new[] { "card", "colorless_card", "relic", "potion", "card_removal" };

    /// <summary>prompt_options.kind_of_item values.</summary>
    public static readonly IReadOnlyList<string> PromptItemKindIds = new[] { "card", "relic", "potion", "bundle", "index" };

    private static readonly Lazy<VocabList> CardsL = new(() => new VocabList("cards",
        CardRegistry.AllIds.Concat(CardCatalog.All.Select(e => e.Id))));
    private static readonly Lazy<VocabList> RelicsL = new(() => new VocabList("relics", RelicRegistry.AllIds));
    private static readonly Lazy<VocabList> PotionsL = new(() => new VocabList("potions", PotionRegistry.AllIds));
    private static readonly Lazy<VocabList> PowersL = new(() => new VocabList("powers", PowerRegistry.AllIds));
    private static readonly Lazy<VocabList> MonstersL = new(() => new VocabList("monsters", MonsterRegistry.AllIds));
    private static readonly Lazy<VocabList> EncountersL = new(() => new VocabList("encounters", EncounterRegistry.AllIds));
    // The two unported events become SimStubEvent instances that carry their real id; list them so they are not "?".
    private static readonly Lazy<VocabList> EventsL = new(() => new VocabList("events",
        EventRegistry.AllIds.Concat(new[] { UnportedEventRules.CrystalSphereId, UnportedEventRules.FakeMerchantId })));
    private static readonly Lazy<VocabList> OrbsL = new(() => new VocabList("orbs", OrbRegistry.AllIds));
    private static readonly Lazy<VocabList> EnchantmentsL = new(() => new VocabList("enchantments", EnchantmentRegistry.AllIds));
    private static readonly Lazy<VocabList> AfflictionsL = new(() => new VocabList("afflictions", AfflictionRegistry.AllIds));
    private static readonly Lazy<VocabList> ActsL = new(() => new VocabList("acts", ActRegistry.AllIds));
    private static readonly Lazy<VocabList> CharactersL = new(() => new VocabList("characters", CharacterRegistry.AllIds));
    private static readonly Lazy<VocabList> CardVarsL = new(() => new VocabList("card_vars", CollectCardVarNames()));

    private static readonly Lazy<VocabList> RestOptionL = new(() => new VocabList("RestOption", RestOptionIds));
    private static readonly Lazy<VocabList> CardRewardAltL = new(() => new VocabList("CardRewardAlt", CardRewardAltIds));
    private static readonly Lazy<VocabList> ShopCategoryL = new(() => new VocabList("ShopCategory", ShopCategoryIds));
    private static readonly Lazy<VocabList> PromptItemKindL = new(() => new VocabList("PromptItemKind", PromptItemKindIds));

    /// <summary>UnplayableReason flag names (hand.playable_reason_id holds the raw bitmask; see <see cref="PlayableReasonBits"/>).</summary>
    public static readonly IReadOnlyList<string> PlayableReasons = Enum.GetValues<UnplayableReason>()
        .OrderBy(v => (int)v).Select(v => v.ToString()).ToArray();

    public static readonly IReadOnlyDictionary<string, int> PlayableReasonBits = Enum.GetValues<UnplayableReason>()
        .OrderBy(v => (int)v).ToDictionary(v => v.ToString(), v => (int)v);

    public static VocabList Cards => CardsL.Value;
    public static VocabList Relics => RelicsL.Value;
    public static VocabList Potions => PotionsL.Value;
    public static VocabList Powers => PowersL.Value;
    public static VocabList Monsters => MonstersL.Value;
    public static VocabList Encounters => EncountersL.Value;
    public static VocabList Events => EventsL.Value;
    public static VocabList Orbs => OrbsL.Value;
    public static VocabList Enchantments => EnchantmentsL.Value;
    public static VocabList Afflictions => AfflictionsL.Value;
    public static VocabList Acts => ActsL.Value;
    public static VocabList Characters => CharactersL.Value;
    public static VocabList CardVars => CardVarsL.Value;
    public static VocabList RestOptions => RestOptionL.Value;
    public static VocabList CardRewardAlts => CardRewardAltL.Value;
    public static VocabList ShopCategories => ShopCategoryL.Value;
    public static VocabList PromptItemKinds => PromptItemKindL.Value;

    /// <summary>The id vocabularies in schema order (key = JSON name under "vocab").</summary>
    public static IReadOnlyList<VocabList> Lists => new[]
    {
        Cards, Relics, Potions, Powers, Monsters, Encounters, Events, Orbs, Enchantments, Afflictions, Acts, Characters,
    };

    private static readonly Lazy<IReadOnlyList<(string Name, IReadOnlyList<string> Values)>> EnumsL = new(BuildEnums);

    /// <summary>Enum lists in schema order (key = JSON name under "vocab.enums").</summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<string> Values)> Enums => EnumsL.Value;

    public static int Card(string? id) => Cards.Index(id);
    public static int Relic(string? id) => Relics.Index(id);
    public static int Potion(string? id) => Potions.Index(id);
    public static int Power(string? id) => Powers.Index(id);
    public static int Monster(string? id) => Monsters.Index(id);
    public static int Encounter(string? id) => Encounters.Index(id);
    public static int Event(string? id) => Events.Index(id);
    public static int Orb(string? id) => Orbs.Index(id);
    public static int Enchantment(string? id) => Enchantments.Index(id);
    public static int Affliction(string? id) => Afflictions.Index(id);
    public static int Act(string? id) => Acts.Index(id);
    public static int Character(string? id) => Characters.Index(id);
    public static int CardVar(string? name) => CardVars.Index(name);
    public static int RestOption(string? id) => RestOptions.Index(id);
    public static int CardRewardAlt(string? id) => CardRewardAlts.Index(id);
    public static int ShopCategory(string? id) => ShopCategories.Index(id);
    public static int PromptItemKind(string? id) => PromptItemKinds.Index(id);

    /// <summary>Flat index of a (card_id, upgrade) pair in the count-vector buffers: card_id * 2 + min(upgrade, 1).</summary>
    public static int CountIndex(int cardId, int upgrade) => cardId * 2 + (upgrade > 0 ? 1 : 0);

    private static IReadOnlyList<(string, IReadOnlyList<string>)> BuildEnums() => new (string, IReadOnlyList<string>)[]
    {
        ("RunPhase", EnumNames<RunPhase>()),
        ("RunActionKind", EnumNames<RunActionKind>()),
        ("IntentType", EnumNames<IntentType>()),
        ("MapPointType", EnumNames<MapPointType>()),
        ("RewardType", EnumNames<RewardType>()),
        ("ChoicePromptKind", EnumNames<ChoicePromptKind>()),
        ("CardType", EnumNames<CardType>()),
        ("TargetType", EnumNames<TargetType>()),
        ("CardRarity", EnumNames<CardRarity>()),
        ("RestOption", RestOptions.Names),
        ("CardRewardAlt", CardRewardAlts.Names),
        ("ShopCategory", ShopCategories.Names),
        // Additions beyond the §D3.2 list (needed to decode frozen columns):
        ("PileType", EnumNames<PileType>()),
        ("CardKeyword", EnumNames<CardKeyword>()),
        ("RelicStatus", EnumNames<RelicStatus>()),
        ("PromptItemKind", PromptItemKinds.Names),
        ("EndKind", EnumNames<EndKind>()),
    };

    /// <summary>Names indexed by underlying value; requires values 0..n-1 without gaps.</summary>
    public static IReadOnlyList<string> EnumNames<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>().Select(v => (Value: Convert.ToInt32(v), Name: v.ToString())).OrderBy(p => p.Value).ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Value != i)
            {
                throw new InvalidOperationException($"enum {typeof(T).Name} is not contiguous from 0 (value {values[i].Value} at {i})");
            }
        }
        return values.Select(p => p.Name).ToArray();
    }

    private static IEnumerable<string> CollectCardVarNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in CardRegistry.AllIds)
        {
            DynamicVarSet vars;
            try
            {
                vars = CardRegistry.Get(id).CreateVars();
            }
            catch (Exception e) when (e is NotImplementedException or NotSupportedException)
            {
                continue;
            }
            foreach (var v in vars.All)
            {
                names.Add(v.Name);
            }
        }
        return names;
    }
}
