namespace Sts2Sim.Core.Run;

public sealed class ContentFilter
{
    public static readonly ContentFilter All = new("all", null, null, null, null, null);

    public static readonly ContentFilter PortedOnly = new(
        "ported",
        id => EncounterRegistry.Defs.Contains(id),
        id => EventRegistry.Models.Contains(id),
        id => CardRegistry.Defs.Contains(id),
        id => RelicRegistry.Models.Contains(id),
        id => PotionRegistry.Models.Contains(id));

    private readonly Func<string, bool>? _encounters;
    private readonly Func<string, bool>? _events;
    private readonly Func<string, bool>? _cards;
    private readonly Func<string, bool>? _relics;
    private readonly Func<string, bool>? _potions;

    public ContentFilter(string name, Func<string, bool>? encounters, Func<string, bool>? events, Func<string, bool>? cards, Func<string, bool>? relics, Func<string, bool>? potions)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        _encounters = encounters;
        _events = events;
        _cards = cards;
        _relics = relics;
        _potions = potions;
    }

    public string Name { get; }

    public ContentFilter Without(string name, IEnumerable<string>? encounters = null, IEnumerable<string>? events = null, IEnumerable<string>? cards = null, IEnumerable<string>? relics = null, IEnumerable<string>? potions = null)
    {
        var e = new HashSet<string>(encounters ?? Array.Empty<string>(), StringComparer.Ordinal);
        var v = new HashSet<string>(events ?? Array.Empty<string>(), StringComparer.Ordinal);
        var c = new HashSet<string>(cards ?? Array.Empty<string>(), StringComparer.Ordinal);
        var r = new HashSet<string>(relics ?? Array.Empty<string>(), StringComparer.Ordinal);
        var p = new HashSet<string>(potions ?? Array.Empty<string>(), StringComparer.Ordinal);
        return new ContentFilter(
            name,
            id => !e.Contains(id) && AllowsEncounter(id),
            id => !v.Contains(id) && AllowsEvent(id),
            id => !c.Contains(id) && AllowsCard(id),
            id => !r.Contains(id) && AllowsRelic(id),
            id => !p.Contains(id) && AllowsPotion(id));
    }

    public bool AllowsEncounter(string id) => _encounters?.Invoke(id) ?? true;

    public bool AllowsEvent(string id) => _events?.Invoke(id) ?? true;

    public bool AllowsCard(string id) => _cards?.Invoke(id) ?? true;

    public bool AllowsRelic(string id) => _relics?.Invoke(id) ?? true;

    public bool AllowsPotion(string id) => _potions?.Invoke(id) ?? true;
}
