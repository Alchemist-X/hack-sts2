using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Sts2Sim.Server;

// Human-readable names and rules text for observations (cards, relics, potions, powers, keywords, enemies, events).
//
// Source of truth: the game's own localisation templates, exported to knowledge/source/<version>/*.jsonl
// (v0.107.1 by scripts/export_sts2_knowledge.py; newer versions by scripts/export_sts2_localization.py).
// Every version directory found is loaded oldest first and newer records replace older ones per id, so a
// kind missing from the newest export falls back to the older one.
//
// Templates use the game's SmartFormat dialect; Format() below implements the subset the templates use
// ({Var}, {Var:diff()}, plural, show (IfUpgraded), cond, choose, implicit conditionals, energyIcons, starIcons,
// percentMore/Less, abs). Numbers come from the live model: a card instance's dynamic vars, a relic/potion
// model's dynamic vars, an event model's public members. A value that cannot be resolved renders as '?'; a
// branching placeholder whose selector cannot be resolved takes its last (default) branch ("not in combat",
// "single player"). Markup ([gold], [blue], [img]..) is stripped, energy renders as "[E]" (e.g. "2[E]") and
// stars as "[Star]", and the result is one line. Every lookup returns null when nothing is known.
public static class RulesText
{
    public const string EnergyIcon = "[E]";
    public const string StarIcon = "[Star]";

    private sealed class Entry
    {
        public string Id = "";
        public string? Title;
        public string? Template;
        public string? Smart;
        public string? Version;
        public Dictionary<string, string>? Moves;
        public Dictionary<string, (string? Title, string? Description)>? Options;
        public Dictionary<string, string>? Pages;
    }

    private sealed class Tables
    {
        public readonly Dictionary<string, Entry> Cards = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Relics = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Potions = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Powers = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Keywords = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Enemies = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Events = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Entry>? ByKind(string kind) => kind switch
        {
            "cards" or "card" => Cards,
            "relics" or "relic" => Relics,
            "potions" or "potion" => Potions,
            "powers" or "power" => Powers,
            "keywords" or "keyword" => Keywords,
            "enemies" or "enemy" => Enemies,
            "events" or "event" => Events,
            _ => null
        };
    }

    public static readonly string[] Kinds = { "cards", "relics", "potions", "powers", "events", "enemies", "keywords" };

    private static Tables _t = new();
    private static readonly ConcurrentDictionary<string, string?> RelicTextCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string?> PotionTextCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string?> CardTextCache = new(StringComparer.Ordinal);

    public static bool Loaded { get; private set; }

    // The knowledge/source directory that was loaded, and the version directories it held (oldest first).
    public static string? Root { get; private set; }

    public static IReadOnlyList<string> Versions { get; private set; } = Array.Empty<string>();

    // One line describing what Load() found, also written to stderr.
    public static string Summary { get; private set; } = "rules text: not loaded";

    // knowledgeDir: a knowledge/ dir, a knowledge/source dir, or one version dir; null to auto-discover
    // (env STS2SIM_KNOWLEDGE, then walk up from the current directory and the binary's directory).
    // Never throws.
    public static void Load(string? knowledgeDir)
    {
        try
        {
            LoadCore(knowledgeDir);
        }
        catch (Exception ex)
        {
            _t = new Tables();
            Loaded = false;
            Root = null;
            Versions = Array.Empty<string>();
            Summary = "rules text: load failed (" + ex.GetType().Name + ": " + ex.Message + ")";
        }
        ClearCaches();
        try
        {
            Console.Error.WriteLine(Summary);
        }
        catch (Exception)
        {
        }
    }

    private static void ClearCaches()
    {
        RelicTextCache.Clear();
        PotionTextCache.Clear();
        CardTextCache.Clear();
    }

    private static void LoadCore(string? knowledgeDir)
    {
        string? explicitDir = knowledgeDir;
        string how = "--knowledge";
        if (string.IsNullOrWhiteSpace(explicitDir))
        {
            explicitDir = Environment.GetEnvironmentVariable("STS2SIM_KNOWLEDGE");
            how = "STS2SIM_KNOWLEDGE";
        }
        List<string> versionDirs;
        string? root;
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            (root, versionDirs) = ResolveExplicit(explicitDir);
            if (versionDirs.Count == 0)
            {
                SetEmpty($"rules text: nothing found at {how} '{explicitDir}'");
                return;
            }
        }
        else
        {
            how = "discovered";
            root = Discover();
            if (root == null)
            {
                SetEmpty("rules text: no knowledge/source/<version> directory found (use --knowledge DIR or STS2SIM_KNOWLEDGE)");
                return;
            }
            versionDirs = VersionDirs(root);
        }
        var tables = new Tables();
        foreach (var dir in versionDirs)
        {
            string version = Path.GetFileName(dir);
            LoadFile(Path.Combine(dir, "cards.jsonl"), version, tables.Cards, ReadSimple);
            LoadFile(Path.Combine(dir, "relics.jsonl"), version, tables.Relics, ReadSimple);
            LoadFile(Path.Combine(dir, "potions.jsonl"), version, tables.Potions, ReadSimple);
            LoadFile(Path.Combine(dir, "keywords.jsonl"), version, tables.Keywords, ReadSimple);
            LoadFile(Path.Combine(dir, "powers.jsonl"), version, tables.Powers, ReadSimple);
            LoadFile(Path.Combine(dir, "enemies.jsonl"), version, tables.Enemies, ReadEnemy);
            LoadFile(Path.Combine(dir, "events.jsonl"), version, tables.Events, ReadEvent);
        }
        _t = tables;
        Root = root;
        Versions = versionDirs.Select(Path.GetFileName).Select(s => s!).ToArray();
        int total = tables.Cards.Count + tables.Relics.Count + tables.Potions.Count + tables.Powers.Count + tables.Keywords.Count + tables.Enemies.Count + tables.Events.Count;
        Loaded = total > 0;
        Summary = string.Create(CultureInfo.InvariantCulture,
            $"rules text: {(Loaded ? "loaded" : "empty")} from {root} ({how}; versions {string.Join(",", Versions)}): " +
            $"{tables.Cards.Count} cards, {tables.Relics.Count} relics, {tables.Potions.Count} potions, {tables.Powers.Count} powers, " +
            $"{tables.Events.Count} events, {tables.Enemies.Count} enemies, {tables.Keywords.Count} keywords");
    }

    private static void SetEmpty(string summary)
    {
        _t = new Tables();
        Loaded = false;
        Root = null;
        Versions = Array.Empty<string>();
        Summary = summary;
    }

    private static (string? Root, List<string> Dirs) ResolveExplicit(string dir)
    {
        string full = Path.GetFullPath(dir);
        if (!Directory.Exists(full))
        {
            return (null, new List<string>());
        }
        if (File.Exists(Path.Combine(full, "cards.jsonl")) || File.Exists(Path.Combine(full, "powers.jsonl")))
        {
            return (full, new List<string> { full });
        }
        foreach (var candidate in new[] { full, Path.Combine(full, "source"), Path.Combine(full, "knowledge", "source") })
        {
            var dirs = VersionDirs(candidate);
            if (dirs.Count > 0)
            {
                return (candidate, dirs);
            }
        }
        return (null, new List<string>());
    }

    private static string? Discover()
    {
        var starts = new List<string>();
        try
        {
            starts.Add(Directory.GetCurrentDirectory());
        }
        catch (Exception)
        {
        }
        starts.Add(AppContext.BaseDirectory);
        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "knowledge", "source");
                if (VersionDirs(candidate).Count > 0)
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }

    // Version directories (v<major>.<minor>.<patch>) under root, oldest first.
    private static List<string> VersionDirs(string root)
    {
        var list = new List<(Version V, string Dir)>();
        if (!Directory.Exists(root))
        {
            return new List<string>();
        }
        foreach (var dir in Directory.GetDirectories(root))
        {
            string name = Path.GetFileName(dir);
            if (name.Length > 1 && name[0] == 'v' && Version.TryParse(name[1..], out var v)
                && Directory.EnumerateFiles(dir, "*.jsonl").Any())
            {
                list.Add((v, dir));
            }
        }
        return list.OrderBy(x => x.V).Select(x => x.Dir).ToList();
    }

    private static void LoadFile(string path, string version, Dictionary<string, Entry> into, Func<JsonElement, Entry?> read)
    {
        if (!File.Exists(path))
        {
            return;
        }
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = read(doc.RootElement);
                if (e != null && e.Id.Length > 0)
                {
                    e.Version = version;
                    into[e.Id] = e;
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private static JsonElement? Eng(JsonElement root)
        => root.TryGetProperty("localized", out var loc) && loc.ValueKind == JsonValueKind.Object
           && loc.TryGetProperty("eng", out var eng) && eng.ValueKind == JsonValueKind.Object ? eng : null;

    private static string? S(JsonElement o, string key)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Entry? ReadSimple(JsonElement root)
    {
        if (Eng(root) is not { } eng)
        {
            return null;
        }
        return new Entry
        {
            Id = S(root, "id") ?? "",
            Title = S(eng, "title"),
            Template = S(eng, "description_template"),
            Smart = S(eng, "smart_template")
        };
    }

    private static Entry? ReadEnemy(JsonElement root)
    {
        var e = ReadSimple(root);
        if (e == null || Eng(root) is not { } eng)
        {
            return e;
        }
        if (eng.TryGetProperty("move_titles", out var moves) && moves.ValueKind == JsonValueKind.Object)
        {
            e.Moves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in moves.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    e.Moves[p.Name] = p.Value.GetString()!;
                }
            }
        }
        return e;
    }

    private static Entry? ReadEvent(JsonElement root)
    {
        var e = ReadSimple(root);
        if (e == null || Eng(root) is not { } eng)
        {
            return e;
        }
        if (eng.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object)
        {
            e.Options = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in options.EnumerateObject())
            {
                e.Options[p.Name] = (S(p.Value, "title"), S(p.Value, "description"));
            }
        }
        if (eng.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Object)
        {
            e.Pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pages.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } text)
                {
                    e.Pages[p.Name] = text;
                }
            }
        }
        return e;
    }

    // ---- lookups ----

    private static Entry? Find(Dictionary<string, Entry> table, string? id, params string[] suffixes)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        if (table.TryGetValue(id, out var e))
        {
            return e;
        }
        foreach (var suffix in suffixes)
        {
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && table.TryGetValue(id[..^suffix.Length], out e))
            {
                return e;
            }
            if (table.TryGetValue(id + suffix, out e))
            {
                return e;
            }
        }
        return null;
    }

    public static string? CardTitle(string cardId) => Find(_t.Cards, cardId)?.Title;

    // Description with {Var} placeholders filled from the instance (upgrades, enchantments, live vars).
    public static string? CardText(CardInstance card)
    {
        if (Find(_t.Cards, card.Id)?.Template is not { } template)
        {
            return null;
        }
        return Format(template, new CardScope(card));
    }

    // Text of a fresh (out-of-combat) copy of a card at the given upgrade level; null for unported cards.
    public static string? CardText(string cardId, int upgradeLevel = 0)
    {
        if (Find(_t.Cards, cardId)?.Template is not { } template)
        {
            return null;
        }
        string key = cardId + "+" + upgradeLevel.ToString(CultureInfo.InvariantCulture);
        return CardTextCache.GetOrAdd(key, _ =>
        {
            if (!CardRegistry.TryGet(cardId, out var def) || def is UnportedCardDef)
            {
                return Format(template, NoScope.Instance);
            }
            var card = new CardInstance(def, CardInstance.NoTarget);
            for (int i = 0; i < upgradeLevel && card.UpgradeLevel < card.MaxUpgradeLevel; i++)
            {
                card.Upgrade();
            }
            return Format(template, new CardScope(card));
        });
    }

    public static string? RelicTitle(string relicId) => Find(_t.Relics, relicId)?.Title;

    public static string? RelicText(string relicId)
    {
        if (Find(_t.Relics, relicId)?.Template is not { } template)
        {
            return null;
        }
        return RelicTextCache.GetOrAdd(relicId, id =>
        {
            RelicModel? model = null;
            try
            {
                if (RelicRegistry.Models.Contains(id))
                {
                    model = RelicRegistry.Create(id);
                }
            }
            catch (Exception)
            {
                model = null;
            }
            return Format(template, model != null ? new VarsScope(model.DynamicVars) : NoScope.Instance);
        });
    }

    // Uses the instance's own dynamic vars (counters that grow over the run render their current value).
    public static string? RelicText(RelicModel relic)
    {
        if (Find(_t.Relics, relic.Id)?.Template is not { } template)
        {
            return null;
        }
        return Format(template, new VarsScope(relic.DynamicVars));
    }

    public static string? PotionTitle(string potionId) => Find(_t.Potions, potionId)?.Title;

    public static string? PotionText(string potionId)
    {
        if (Find(_t.Potions, potionId)?.Template is not { } template)
        {
            return null;
        }
        return PotionTextCache.GetOrAdd(potionId, id =>
        {
            PotionModel? model = null;
            try
            {
                if (PotionRegistry.Models.Contains(id))
                {
                    model = PotionRegistry.Create(id);
                }
            }
            catch (Exception)
            {
                model = null;
            }
            return Format(template, model != null ? new VarsScope(model.DynamicVars, model.TargetType.ToString()) : NoScope.Instance);
        });
    }

    // Power id as the sim reports it (STRENGTH_POWER) or without the suffix (STRENGTH).
    public static string? PowerTitle(string powerId)
    {
        if (Find(_t.Powers, powerId, "_POWER")?.Title is { } title)
        {
            return title;
        }
        return TemporaryOrigin(powerId) is { } origin ? AnyTitle(origin) : null;
    }

    // The power's description with its amount written as "N" (the amount is shown next to the id in
    // observations). The amount-aware smartDescription is preferred: the static description sometimes
    // hard-codes the base amount (RAVENOUS: "gaining 1 Strength" while the live power has 4).
    public static string? PowerText(string powerId) => PowerTextCore(powerId, null);

    // The power's description with {Amount} filled in.
    public static string? PowerText(string powerId, int amount) => PowerTextCore(powerId, amount);

    private static string? PowerTextCore(string powerId, int? amount)
    {
        var e = PowerEntry(powerId);
        if (e == null)
        {
            return null;
        }
        var scope = new PowerScope(amount);
        if (e.Smart is { Length: > 0 } smart)
        {
            return Format(smart, scope);
        }
        return e.Template is { Length: > 0 } template ? Format(template, scope) : null;
    }

    private static Entry? PowerEntry(string powerId)
    {
        if (Find(_t.Powers, powerId, "_POWER") is { } e && (e.Template != null || e.Smart != null))
        {
            return e;
        }
        // Temporary powers (SETUP_STRIKE_POWER, ...) borrow the generic TEMPORARY_<X>_POWER / _DOWN text.
        try
        {
            var model = PowerRegistry.Create(powerId);
            if (model is ITemporaryPower temp)
            {
                string applied = temp.InternallyAppliedPowerId;
                string stem = applied.EndsWith("_POWER", StringComparison.Ordinal) ? applied[..^6] : applied;
                string key = model.Type == PowerType.Debuff ? "TEMPORARY_" + stem + "_DOWN" : "TEMPORARY_" + stem;
                return Find(_t.Powers, key, "_POWER");
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static string? TemporaryOrigin(string powerId)
    {
        try
        {
            return PowerRegistry.Create(powerId) is ITemporaryPower temp ? temp.OriginModelId : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? AnyTitle(string id)
        => CardTitle(id) ?? RelicTitle(id) ?? PotionTitle(id);

    // An enchantment's title. knowledge/source has no enchantment table; the game's titles are the id in title
    // case (PERFECT_FIT -> "Perfect Fit", SOWN -> "Sown"), so registered enchantment ids render that way.
    public static string? EnchantmentTitle(string id)
    {
        if (string.IsNullOrEmpty(id) || !EnchantmentRegistry.Models.Contains(id))
        {
            return null;
        }
        return string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    // Keyword as the sim spells it (exhaust, Exhaust, EXHAUST) or a power-like keyword (VULNERABLE).
    public static string? KeywordText(string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            return null;
        }
        string id = keyword.Replace(' ', '_').ToUpperInvariant();
        if (Find(_t.Keywords, id)?.Template is not { } template)
        {
            return null;
        }
        return Format(template, NoScope.Instance);
    }

    public static string? KeywordTitle(string keyword)
        => string.IsNullOrEmpty(keyword) ? null : Find(_t.Keywords, keyword.Replace(' ', '_').ToUpperInvariant())?.Title;

    // Monsters without their own localisation (DECIMILLIPEDE_SEGMENT_FRONT) fall back to the longest
    // '_'-separated prefix that has one (DECIMILLIPEDE).
    public static string? EnemyTitle(string monsterId)
    {
        if (Find(_t.Enemies, monsterId)?.Title is { } title)
        {
            return title;
        }
        string id = monsterId ?? "";
        for (int cut = id.LastIndexOf('_'); cut > 0; cut = id.LastIndexOf('_', cut - 1))
        {
            if (Find(_t.Enemies, id[..cut])?.Title is { } prefixTitle)
            {
                return prefixTitle;
            }
        }
        return null;
    }

    public static string? EnemyMoveTitle(string monsterId, string moveId)
    {
        if (Find(_t.Enemies, monsterId)?.Moves is not { } moves || string.IsNullOrEmpty(moveId))
        {
            return null;
        }
        if (moves.TryGetValue(moveId, out var title))
        {
            return title;
        }
        return moveId.EndsWith("_MOVE", StringComparison.OrdinalIgnoreCase) && moves.TryGetValue(moveId[..^5], out title) ? title : null;
    }

    public static IReadOnlyDictionary<string, string>? EnemyMoves(string monsterId) => Find(_t.Enemies, monsterId)?.Moves;

    public static string? EventTitle(string eventId) => Find(_t.Events, eventId)?.Title;

    // "Title: description" of an event option (TextKey "<EVENT>.pages.<PAGE>.options.<KEY>"), placeholders '?'.
    public static string? EventOptionText(string eventId, string optionKey) => EventOptionText(eventId, optionKey, null, null, null);

    // As above, with placeholders filled from the live event model's public members ({Gold}, {HpLoss}, costs)
    // and the option's relic ({Relic}).
    public static string? EventOptionText(EventModel ev, EventOption option) => EventOptionText(ev.Id, option.TextKey, ev, option, null);

    // As above, with the run for vars the game computes from run state (DenseVegetation {Heal}: the rest-site heal).
    public static string? EventOptionText(EventModel ev, EventOption option, RunState? rs) => EventOptionText(ev.Id, option.TextKey, ev, option, rs);

    private static string? EventOptionText(string eventId, string optionKey, EventModel? ev, EventOption? option, RunState? rs = null)
    {
        if (string.IsNullOrEmpty(optionKey))
        {
            return null;
        }
        string prefix = eventId + ".pages.";
        string key = optionKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? optionKey[prefix.Length..] : optionKey;
        var e = Find(_t.Events, eventId);
        if (e?.Options != null && e.Options.TryGetValue(key, out var opt))
        {
            IScope scope = ev != null ? new EventScope(ev, option, rs) : NoScope.Instance;
            string? title = opt.Title is { Length: > 0 } t ? Format(t, scope) : null;
            string? desc = opt.Description is { Length: > 0 } d ? Format(d, scope) : null;
            if (title == null && desc == null)
            {
                return null;
            }
            if (desc == null || desc.Length == 0)
            {
                return title;
            }
            return title == null || title.Length == 0 ? desc : title + ": " + desc;
        }
        // Ancient (and other) relic options: "<EVENT>.pages.<PAGE>.options.<RELIC_ID>" without an option text.
        string last = optionKey[(optionKey.LastIndexOf('.') + 1)..];
        string? relicId = option?.RelicId ?? (RelicTitle(last) != null ? last : null);
        if (relicId != null && RelicTitle(relicId) is { } relicTitle)
        {
            string? relicText = option?.Relic != null ? RelicText(option.Relic) : RelicText(relicId);
            return relicText is { Length: > 0 } ? relicTitle + ": " + relicText : relicTitle;
        }
        return null;
    }

    // The event page's body text the event screen shows above its options (NEventLayout description), from the
    // event's current description key "<EVENT>.pages.<PAGE>.description", with placeholders filled as for options.
    public static string? EventPageText(EventModel ev, RunState? rs = null)
    {
        string? key = ev.Description;
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        string prefix = ev.Id + ".pages.";
        const string suffix = ".description";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string page = key[prefix.Length..^suffix.Length];
        if (Find(_t.Events, ev.Id)?.Pages is not { } pages || !pages.TryGetValue(page, out var template))
        {
            return null;
        }
        string text = Format(template, new EventScope(ev, null, rs));
        return text.Length == 0 ? null : text;
    }

    // ---- catalog ----

    public sealed record CatalogItem(string Id, string? Name, string? Text, IReadOnlyDictionary<string, string>? Moves);

    // All known ids of a kind (union of the sim's registry and the localisation tables), with name and text.
    public static List<CatalogItem>? Catalog(string kind)
    {
        var table = _t.ByKind(kind);
        if (table == null)
        {
            return null;
        }
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in table.Keys)
        {
            ids.Add(id);
        }
        try
        {
            switch (kind)
            {
                case "cards":
                    foreach (var id in CardRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    break;
                case "relics":
                    foreach (var id in RelicRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    break;
                case "potions":
                    foreach (var id in PotionRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    break;
                case "powers":
                    ids.Clear();
                    foreach (var id in PowerRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    foreach (var id in table.Keys)
                    {
                        if (!ids.Contains(id + "_POWER") && !ids.Contains(id))
                        {
                            ids.Add(id);
                        }
                    }
                    break;
                case "events":
                    foreach (var id in EventRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    break;
                case "enemies":
                    foreach (var id in MonsterRegistry.AllIds)
                    {
                        ids.Add(id);
                    }
                    break;
            }
        }
        catch (Exception)
        {
        }
        var items = new List<CatalogItem>(ids.Count);
        foreach (var id in ids)
        {
            items.Add(Item(kind, id));
        }
        return items;
    }

    public static CatalogItem Item(string kind, string id)
    {
        string? Safe(Func<string?> f)
        {
            try
            {
                return f();
            }
            catch (Exception)
            {
                return null;
            }
        }
        return kind switch
        {
            "cards" => new CatalogItem(id, CardTitle(id), Safe(() => CardText(id)), null),
            "relics" => new CatalogItem(id, RelicTitle(id), Safe(() => RelicText(id)), null),
            "potions" => new CatalogItem(id, PotionTitle(id), Safe(() => PotionText(id)), null),
            "powers" => new CatalogItem(id, Safe(() => PowerTitle(id)), Safe(() => PowerText(id)), null),
            "keywords" => new CatalogItem(id, KeywordTitle(id), Safe(() => KeywordText(id)), null),
            "enemies" => new CatalogItem(id, EnemyTitle(id), null, EnemyMoves(id)),
            "events" => new CatalogItem(id, EventTitle(id), null, null),
            _ => new CatalogItem(id, null, null, null)
        };
    }

    // Every kind under which an id is known (registry or localisation), for 'describe'.
    public static List<(string Kind, CatalogItem Item)> Lookup(string id)
    {
        var found = new List<(string, CatalogItem)>();
        if (string.IsNullOrEmpty(id))
        {
            return found;
        }
        string up = id.ToUpperInvariant();
        bool Known(string kind)
        {
            try
            {
                return kind switch
                {
                    "cards" => _t.Cards.ContainsKey(up) || CardRegistry.TryGet(up, out _),
                    "relics" => _t.Relics.ContainsKey(up) || RelicRegistry.Models.Contains(up),
                    "potions" => _t.Potions.ContainsKey(up) || PotionRegistry.Models.Contains(up),
                    "powers" => Find(_t.Powers, up, "_POWER") != null || PowerRegistry.AllIds.Contains(up) || PowerRegistry.AllIds.Contains(up + "_POWER"),
                    "keywords" => _t.Keywords.ContainsKey(up),
                    "enemies" => _t.Enemies.ContainsKey(up) || MonsterRegistry.TryGet(up, out _),
                    "events" => _t.Events.ContainsKey(up) || EventRegistry.AllIds.Contains(up),
                    _ => false
                };
            }
            catch (Exception)
            {
                return false;
            }
        }
        foreach (var kind in Kinds)
        {
            if (Known(kind))
            {
                string key = kind == "powers" && !PowerRegistryHas(up) && PowerRegistryHas(up + "_POWER") ? up + "_POWER" : up;
                found.Add((kind, Item(kind, key)));
            }
        }
        return found;
    }

    private static bool PowerRegistryHas(string id)
    {
        try
        {
            return PowerRegistry.AllIds.Contains(id);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- template formatting ----

    private readonly struct Val
    {
        public Val(decimal? num, string? str, bool? flag)
        {
            Num = num;
            Str = str;
            Flag = flag;
        }

        public decimal? Num { get; }
        public string? Str { get; }
        public bool? Flag { get; }

        public bool Truthy => Flag ?? (Num.HasValue ? Num.Value != 0m : !string.IsNullOrEmpty(Str));

        public string Render() => Num is { } n ? N(n) : Str ?? (Flag is { } f ? (f ? "true" : "false") : "?");
    }

    private interface IScope
    {
        Val? Get(string name);

        bool Upgraded { get; }
    }

    private sealed class NoScope : IScope
    {
        public static readonly NoScope Instance = new();

        public Val? Get(string name) => null;

        public bool Upgraded => false;
    }

    private sealed class PowerScope : IScope
    {
        private readonly int? _amount;

        public PowerScope(int? amount) => _amount = amount;

        public Val? Get(string name)
        {
            if (name.Equals("Amount", StringComparison.OrdinalIgnoreCase) || name.Equals("DisplayAmount", StringComparison.OrdinalIgnoreCase))
            {
                return _amount is { } a ? new Val(a, null, null) : new Val(null, "N", null);
            }
            return name is "OwnerName" or "Owner" ? new Val(null, "this creature", null) : null;
        }

        public bool Upgraded => false;
    }

    private static Val? FromVar(DynamicVar v) => v switch
    {
        StringVar s => new Val(null, s.StringValue, null),
        BoolVar b => new Val(null, null, b.BoolVal),
        _ => new Val(v.BaseValue, null, null)
    };

    private static DynamicVar? FindVar(DynamicVarSet vars, string name)
    {
        var all = vars.All;
        DynamicVar? Match(string n)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Name, n, StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }
            return null;
        }
        return Match(name)
            ?? (name.EndsWith("Power", StringComparison.OrdinalIgnoreCase) && name.Length > 5 ? Match(name[..^5]) : Match(name + "Power"));
    }

    private sealed class VarsScope : IScope
    {
        private readonly DynamicVarSet _vars;
        private readonly string? _targetType;

        public VarsScope(DynamicVarSet vars, string? targetType = null)
        {
            _vars = vars;
            _targetType = targetType;
        }

        public Val? Get(string name)
        {
            if (FindVar(_vars, name) is { } v)
            {
                return FromVar(v);
            }
            if (_targetType != null && name == "TargetType")
            {
                return new Val(null, _targetType, null);
            }
            return null;
        }

        public bool Upgraded => false;
    }

    private sealed class CardScope : IScope
    {
        private readonly CardInstance _card;

        public CardScope(CardInstance card) => _card = card;

        public Val? Get(string name)
        {
            if (FindVar(_card.Vars, name) is { } v && v is not IfUpgradedVar)
            {
                return FromVar(v);
            }
            switch (name)
            {
                case "IfUpgraded":
                    return new Val(null, null, Upgraded);
                case "TargetType":
                    return new Val(null, _card.TargetType.ToString(), null);
                case "CardType":
                    return new Val(null, _card.Type.ToString(), null);
            }
            return null;
        }

        public bool Upgraded => _card.UpgradeLevel > 0;
    }

    private sealed class EventScope : IScope
    {
        private readonly EventModel _ev;
        private readonly EventOption? _option;
        private readonly RunState? _rs;

        public EventScope(EventModel ev, EventOption? option, RunState? rs = null)
        {
            _ev = ev;
            _option = option;
            _rs = rs;
        }

        public Val? Get(string name)
        {
            if ((name == "Relic" || name == "RelicName") && _option?.RelicId is { } relicId)
            {
                return new Val(null, RelicTitle(relicId) ?? relicId, null);
            }
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy;
            var type = _ev.GetType();
            object? value = null;
            bool found = false;
            try
            {
                // The port names members after the game's vars ({Gold} -> Gold, {HpLoss} -> HpLoss), sometimes
                // with a unit suffix ({Heal} -> HealAmount, {MaxHp} -> MaxHpGain).
                foreach (var candidate in new[] { name, name + "Amount", name + "Gain" })
                {
                    // A var the game computes from run state: "<Name>For(RunState)" (DenseVegetation.HealFor).
                    if (_rs != null && type.GetMethod(candidate + "For", flags, new[] { typeof(RunState) }) is { } computed)
                    {
                        value = computed.Invoke(computed.IsStatic ? null : _ev, new object[] { _rs });
                        found = true;
                        break;
                    }
                    if (type.GetProperty(candidate, flags) is { } prop && prop.GetIndexParameters().Length == 0 && prop.GetGetMethod() is { } getter)
                    {
                        value = prop.GetValue(getter.IsStatic ? null : _ev);
                        found = true;
                        break;
                    }
                    if (type.GetField(candidate, flags) is { } field)
                    {
                        value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(field.IsStatic ? null : _ev);
                        found = true;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            if (!found || value == null)
            {
                return ModelConstant(type, name);
            }
            return value switch
            {
                int i => new Val(i, null, null),
                long l => new Val(l, null, null),
                decimal d => new Val(d, null, null),
                double db => new Val((decimal)db, null, null),
                float f => new Val((decimal)f, null, null),
                bool b => new Val(null, null, b),
                // "<Relic>Description" vars hold the relic's description (TeaMaster.BoneTeaDescription).
                string s when name.EndsWith("Description", StringComparison.Ordinal) && RelicText(s) is { } relicText => new Val(null, relicText, null),
                string s => new Val(null, AnyTitle(s) ?? EnchantmentTitle(s) ?? s, null),
                CardInstance c => new Val(null, CardTitle(c.Id) ?? c.Id, null),
                RelicModel r => new Val(null, RelicTitle(r.Id) ?? r.Id, null),
                PotionModel p => new Val(null, PotionTitle(p.Id) ?? p.Id, null),
                _ => null
            };
        }

        public bool Upgraded => false;

        // {Relic}/{Potion}/{Card}/{Curse} with no same-named member: the event's single public const id of that
        // kind (SunkenStatue.SwordOfStoneId = "SWORD_OF_STONE" -> "Sword of Stone"); null when ambiguous.
        private static Val? ModelConstant(Type type, string name)
        {
            Func<string, string?>? title = name switch
            {
                "Relic" or "RelicName" => RelicTitle,
                "Potion" => PotionTitle,
                "Card" or "Curse" => CardTitle,
                _ => null
            };
            if (title == null)
            {
                return null;
            }
            string? match = null;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && field.FieldType == typeof(string) && field.Name != "EventId" && field.GetRawConstantValue() is string id && title(id) is { } t)
                {
                    if (match != null && match != t)
                    {
                        return null;
                    }
                    match = t;
                }
            }
            return match != null ? new Val(null, match, null) : null;
        }
    }

    private static string N(decimal d)
    {
        decimal r = decimal.Round(d, 2);
        return r == decimal.Truncate(r) ? ((long)r).ToString(CultureInfo.InvariantCulture) : r.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static readonly Regex ImgTag = new(@"\[img\][^\[]*\[/img\]", RegexOptions.Compiled);
    private static readonly Regex MarkupTag = new(@"\[/?[a-z][a-z_]*(?:=[^\]]*)?\]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunct = new(@" ([.,!:;])", RegexOptions.Compiled);

    private static string Format(string template, IScope scope)
    {
        string raw = FormatPart(template, scope, null);
        raw = ImgTag.Replace(raw, string.Empty);
        raw = MarkupTag.Replace(raw, string.Empty);
        raw = raw.Replace(' ', ' ');
        raw = Spaces.Replace(raw, " ").Trim();
        return SpaceBeforePunct.Replace(raw, "$1");
    }

    // Formats text with {placeholders}; `current` is the value an empty selector ({} / {:diff()}) refers to.
    private static string FormatPart(string s, IScope scope, Val? current)
    {
        var sb = new StringBuilder(s.Length + 16);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '{')
            {
                int end = MatchBrace(s, i);
                if (end < 0)
                {
                    sb.Append(s, i, s.Length - i);
                    break;
                }
                sb.Append(Placeholder(s.Substring(i + 1, end - i - 1), scope, current));
                i = end + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static int MatchBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                depth++;
            }
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    // Splits at top-level '|' (outside nested braces).
    private static List<string> SplitBranches(string s)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (c == '|' && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    private static readonly Regex FormatterWithArgs = new(@"^([A-Za-z]+)\(([^)]*)\)(?::(.*))?$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex NamedFormatter = new(@"^(plural|show|cond|list|choose)(?::(.*))?$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static string Placeholder(string body, IScope scope, Val? current)
    {
        int colon = body.IndexOf(':');
        string selector = colon < 0 ? body : body[..colon];
        string? rest = colon < 0 ? null : body[(colon + 1)..];
        Val? value;
        switch (selector)
        {
            case "":
                value = current;
                break;
            case "singleStarIcon":
                return StarIcon;
            case "energyPrefix":
                value = null;
                break;
            case "InCombat":
            case "IsMultiplayer":
                value = new Val(null, null, false);
                break;
            default:
                // "{AncientCard.StringValue:...}" names a member of the var; the var itself carries the value.
                int dot = selector.IndexOf('.');
                value = scope.Get(dot > 0 ? selector[..dot] : selector);
                break;
        }
        if (rest == null)
        {
            return value?.Render() ?? "?";
        }
        string name;
        string args = "";
        string format = "";
        var m = FormatterWithArgs.Match(rest);
        if (m.Success)
        {
            name = m.Groups[1].Value;
            args = m.Groups[2].Value;
            format = m.Groups[3].Success ? m.Groups[3].Value : "";
        }
        else if (NamedFormatter.Match(rest) is { Success: true } nm)
        {
            name = nm.Groups[1].Value;
            format = nm.Groups[2].Success ? nm.Groups[2].Value : "";
        }
        else
        {
            name = "";
            format = rest;
        }
        switch (name)
        {
            case "diff":
            case "inverseDiff":
                return value?.Render() ?? "?";
            case "abs":
                return value?.Num is { } a ? N(Math.Abs(a)) : value?.Render() ?? "?";
            case "percentMore":
                return value?.Num is { } pm ? N(decimal.Round((pm - 1m) * 100m)) : "?";
            case "percentLess":
                return value?.Num is { } pl ? N(decimal.Round((1m - pl) * 100m)) : "?";
            case "energyIcons":
                if (value?.Num is { } en)
                {
                    return N(en) + EnergyIcon;
                }
                if (int.TryParse(args, NumberStyles.Integer, CultureInfo.InvariantCulture, out int literal))
                {
                    return literal == 1 ? EnergyIcon : literal.ToString(CultureInfo.InvariantCulture) + EnergyIcon;
                }
                return "?" + EnergyIcon;
            case "starIcons":
                return value?.Num is { } st ? N(st) + StarIcon : "?" + StarIcon;
            case "plural":
            {
                var branches = SplitBranches(format);
                string pick = value?.Num is { } pn
                    ? (pn == 1m ? branches[0] : branches[^1])
                    : branches[^1];
                return FormatPart(pick, scope, value);
            }
            case "show":
            {
                var branches = SplitBranches(format);
                bool up = value?.Flag ?? scope.Upgraded;
                string pick = up ? branches[0] : (branches.Count > 1 ? branches[1] : "");
                return FormatPart(pick, scope, value);
            }
            case "cond":
                return FormatPart(Cond(format, value), scope, value);
            case "choose":
            {
                var options = args.Split('|');
                var branches = SplitBranches(format);
                string? str = value?.Str ?? value?.Render();
                int idx = value == null ? -1 : Array.FindIndex(options, o => string.Equals(o, str, StringComparison.OrdinalIgnoreCase));
                string pick = idx >= 0 && idx < branches.Count ? branches[idx] : (branches.Count > options.Length ? branches[^1] : "");
                return FormatPart(pick, scope, value);
            }
            case "":
            {
                // Implicit conditional: {Flag:if-true|if-false}; an unknown selector takes the last branch.
                var branches = SplitBranches(format);
                if (branches.Count == 1)
                {
                    return value is { } v1 && v1.Truthy ? FormatPart(branches[0], scope, value) : "";
                }
                string pick = value is { } v && v.Truthy ? branches[0] : branches[^1];
                return FormatPart(pick, scope, value);
            }
            default:
                return value?.Render() ?? "?";
        }
    }

    // {X:cond:<0?neg|>5?big|default}
    private static readonly Regex CondTest = new(@"^(<=|>=|!=|<|>|=)?-?\d+(\.\d+)?\?", RegexOptions.Compiled);

    // {X:cond:<0?neg|>5?big|default} (numeric tests), or {X:cond:if-set|if-empty} (no tests: truthiness;
    // {} inside the first branch is the value itself, e.g. {Character.StringValue:cond:[gold]{}[/gold]|another character}).
    private static string Cond(string format, Val? value)
    {
        var branches = SplitBranches(format);
        if (!branches.Any(b => CondTest.IsMatch(b)))
        {
            return value is { } v && v.Truthy ? branches[0] : (branches.Count > 1 ? branches[^1] : "");
        }
        foreach (var b in branches)
        {
            var m = CondTest.Match(b);
            if (!m.Success)
            {
                return b;
            }
            if (value?.Num is { } n && Test(b[..(m.Length - 1)], n))
            {
                return b[m.Length..];
            }
        }
        return "";
    }

    private static bool Test(string test, decimal n)
    {
        string op = "=";
        foreach (var candidate in new[] { "<=", ">=", "!=", "<", ">", "=" })
        {
            if (test.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                break;
            }
        }
        string num = test.StartsWith(op, StringComparison.Ordinal) ? test[op.Length..] : test;
        if (!decimal.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var rhs))
        {
            return false;
        }
        return op switch
        {
            "<" => n < rhs,
            ">" => n > rhs,
            "<=" => n <= rhs,
            ">=" => n >= rhs,
            "!=" => n != rhs,
            _ => n == rhs
        };
    }
}
