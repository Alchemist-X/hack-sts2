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

    public bool AllowsEncounter(string id) => _encounters?.Invoke(id) ?? true;

    public bool AllowsEvent(string id) => _events?.Invoke(id) ?? true;

    public bool AllowsCard(string id) => _cards?.Invoke(id) ?? true;

    public bool AllowsRelic(string id) => _relics?.Invoke(id) ?? true;

    public bool AllowsPotion(string id) => _potions?.Invoke(id) ?? true;
}
