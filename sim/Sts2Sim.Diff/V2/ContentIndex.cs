namespace Sts2Sim.Diff.V2;

public static class ContentIndex
{
    public const string Card = "card";
    public const string Power = "power";
    public const string Relic = "relic";
    public const string Potion = "potion";
    public const string Monster = "monster";
    public const string Encounter = "encounter";
    public const string Event = "event";
    public const string Enchantment = "enchantment";

    private static readonly Lazy<HashSet<string>> Powers = new(() => new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal));

    private static readonly Dictionary<string, string> ListAreas = new(StringComparer.Ordinal)
    {
        ["hand"] = Card, ["deck"] = Card, ["cards"] = Card, ["candidates"] = Card, ["draw_multiset"] = Card,
        ["discard"] = Card, ["exhaust"] = Card, ["powers"] = Power, ["relics"] = Relic, ["potions"] = Potion
    };

    public static bool IsPorted(string area, string id) => area switch
    {
        Card => CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef,
        Power => Powers.Value.Contains(id),
        Relic => RelicRegistry.Models.Contains(id),
        Potion => PotionRegistry.Models.Contains(id),
        Monster => MonsterRegistry.Defs.Contains(id),
        Encounter => EncounterRegistry.Defs.Contains(id),
        Event => Sts2Sim.Core.Registry.EventRegistry.Models.Contains(id),
        Enchantment => EnchantmentRegistry.Models.Contains(id),
        _ => true
    };

    public static string AreaOfRoomModel(string id)
        => EncounterRegistry.Defs.Contains(id) || id.EndsWith("_WEAK", StringComparison.Ordinal) || id.EndsWith("_NORMAL", StringComparison.Ordinal)
           || id.EndsWith("_ELITE", StringComparison.Ordinal) || id.EndsWith("_BOSS", StringComparison.Ordinal)
            ? Encounter
            : Event;

    private static string CardIdOfKey(string key) => key.EndsWith('+') ? key[..^1] : key;

    public static void Collect(JsonNode? node, string? context, ISet<ContentKey> into)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (value is JsonValue v && v.TryGetValue(out string? s) && s != null)
                    {
                        string id = RunScreenProjector.StripCategory(s);
                        switch (key)
                        {
                            case "monster_id": into.Add(new ContentKey(Monster, id)); break;
                            case "potion_id": into.Add(new ContentKey(Potion, id)); break;
                            case "relic_id": into.Add(new ContentKey(Relic, id)); break;
                            case "event_id": into.Add(new ContentKey(Event, id)); break;
                            case "card_id": into.Add(new ContentKey(Card, CardIdOfKey(id))); break;
                            case "encounter_id": into.Add(new ContentKey(Encounter, id)); break;
                            case "model_id" when context == "room": into.Add(new ContentKey(AreaOfRoomModel(id), id)); break;
                            case "id" when context != null:
                                if (context == "event")
                                {
                                    into.Add(new ContentKey(Event, id));
                                }
                                else if (context == "enchantment")
                                {
                                    into.Add(new ContentKey(Enchantment, id));
                                }
                                else if (ListAreas.TryGetValue(context, out var area))
                                {
                                    into.Add(new ContentKey(area, area == Card ? CardIdOfKey(id) : id));
                                }
                                break;
                        }
                        continue;
                    }
                    string? next = key switch
                    {
                        "event" or "room" or "enchantment" => key,
                        _ when ListAreas.ContainsKey(key) => key,
                        "entries" => "entries",
                        _ => context is "hand" or "deck" or "cards" or "candidates" ? null : context
                    };
                    Collect(value, next, into);
                }
                if (context == "entries" && o["category"] is JsonValue cv && cv.TryGetValue(out string? cat) && o["id"] is JsonValue iv && iv.TryGetValue(out string? eid) && eid != null)
                {
                    string? area = cat switch { "card" or "colorless_card" => Card, "relic" => Relic, "potion" => Potion, _ => null };
                    if (area != null)
                    {
                        into.Add(new ContentKey(area, eid));
                    }
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    if (item is JsonValue v && v.TryGetValue(out string? s) && s != null && context != null && ListAreas.TryGetValue(context, out var area))
                    {
                        string id = RunScreenProjector.StripCategory(s);
                        into.Add(new ContentKey(area, area == Card ? CardIdOfKey(id) : id));
                    }
                    else
                    {
                        Collect(item, context, into);
                    }
                }
                break;
        }
    }

    public static ContentKey? FirstUnported(IEnumerable<ContentKey> keys)
    {
        foreach (var key in keys.OrderBy(k => k.Area, StringComparer.Ordinal).ThenBy(k => k.Id, StringComparer.Ordinal))
        {
            if (!IsPorted(key.Area, key.Id))
            {
                return key;
            }
        }
        return null;
    }
}

public static class Triage
{
    public static string ClassifyException(Exception ex)
    {
        if (ex is NotImplementedException)
        {
            return DivergenceClass.ContentNotPorted;
        }
        if (ex is KeyNotFoundException && ex.Message.Contains("registered", StringComparison.Ordinal))
        {
            return DivergenceClass.ContentNotPorted;
        }
        var frame = new System.Diagnostics.StackTrace(ex, false).GetFrames().FirstOrDefault(f => f.GetMethod()?.DeclaringType != null);
        string? ns = frame?.GetMethod()?.DeclaringType?.Namespace;
        return ns != null && ns.StartsWith("Sts2Sim.Diff", StringComparison.Ordinal) ? DivergenceClass.HarnessBug : DivergenceClass.PortBug;
    }

    public static string ClassifyDivergence(SegmentResult result)
    {
        var d = result.Divergence!;
        if (result.ReplayedIneligible)
        {
            return DivergenceClass.ExporterIneligible;
        }
        if (result.Touched.TryGetValue(d.DecisionIndex, out var touched) && ContentIndex.FirstUnported(touched) is { } unported)
        {
            result.Reason += $" (first unported id {unported})";
            return DivergenceClass.ContentNotPorted;
        }
        if (d.First.Note == RunScreenComparer.NotProjectedNote)
        {
            return DivergenceClass.ProjectionGap;
        }
        if (d.Kind == V2DivergenceKind.Action)
        {
            if (d.First.Path == "action.type")
            {
                return DivergenceClass.OutOfScope;
            }
            if (d.First.Path is "action.prompt" or "action.legal" or "action.picks" or "action.confirm")
            {
                return DivergenceClass.PortBug;
            }
            return d.OracleProjection != null ? DivergenceClass.HarnessBug : DivergenceClass.PortBug;
        }
        return DivergenceClass.PortBug;
    }
}
