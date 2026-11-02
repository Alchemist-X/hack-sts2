using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class CardCatalogEntry
{
    public CardCatalogEntry(string id, CardType type, CardRarity rarity, TargetType targetType, int canonicalCost, bool costsX, CardMultiplayerConstraint multiplayerConstraint, bool canBeGeneratedInCombat, bool canBeGeneratedByModifiers)
    {
        Id = id;
        Type = type;
        Rarity = rarity;
        TargetType = targetType;
        CanonicalCost = canonicalCost;
        CostsX = costsX;
        MultiplayerConstraint = multiplayerConstraint;
        CanBeGeneratedInCombat = canBeGeneratedInCombat;
        CanBeGeneratedByModifiers = canBeGeneratedByModifiers;
    }

    public string Id { get; }

    public CardType Type { get; }

    public CardRarity Rarity { get; }

    public TargetType TargetType { get; }

    public int CanonicalCost { get; }

    public bool CostsX { get; }

    public CardMultiplayerConstraint MultiplayerConstraint { get; }

    public bool CanBeGeneratedInCombat { get; }

    public bool CanBeGeneratedByModifiers { get; }
}

public static partial class CardCatalog
{
    private static readonly Lazy<Table> Lookup = new(BuildTable);

    public static int Count => Lookup.Value.Entries.Count;

    public static IReadOnlyList<CardCatalogEntry> All => Lookup.Value.Entries;

    public static bool Contains(string cardId) => Lookup.Value.ById.ContainsKey(cardId);

    public static CardCatalogEntry Get(string cardId)
        => Lookup.Value.ById.TryGetValue(cardId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Card {cardId} is in no v0.111.0 card pool");

    public static bool TryGet(string cardId, out CardCatalogEntry entry)
    {
        if (Lookup.Value.ById.TryGetValue(cardId, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    public static CardDef Resolve(string cardId)
        => Lookup.Value.Defs.TryGetValue(cardId, out var def)
            ? def
            : throw new KeyNotFoundException($"Card {cardId} is in no v0.111.0 card pool");

    public static bool IsPorted(string cardId) => CardRegistry.Defs.Contains(cardId);

    private static Table BuildTable()
    {
        var entries = Data;
        var byId = new Dictionary<string, CardCatalogEntry>(entries.Length, StringComparer.Ordinal);
        var defs = new Dictionary<string, CardDef>(entries.Length, StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (!byId.TryAdd(entry.Id, entry))
            {
                throw new InvalidOperationException($"Duplicate card catalog entry {entry.Id}");
            }
            defs.Add(entry.Id, CardRegistry.TryGet(entry.Id, out var def) ? def : new UnportedCardDef(entry));
        }
        return new Table(Array.AsReadOnly(entries), byId.ToFrozenDictionary(StringComparer.Ordinal), defs.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private sealed class Table
    {
        public Table(ReadOnlyCollection<CardCatalogEntry> entries, FrozenDictionary<string, CardCatalogEntry> byId, FrozenDictionary<string, CardDef> defs)
        {
            Entries = entries;
            ById = byId;
            Defs = defs;
        }

        public ReadOnlyCollection<CardCatalogEntry> Entries { get; }

        public FrozenDictionary<string, CardCatalogEntry> ById { get; }

        public FrozenDictionary<string, CardDef> Defs { get; }
    }
}
