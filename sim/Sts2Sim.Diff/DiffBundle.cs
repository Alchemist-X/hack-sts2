namespace Sts2Sim.Diff;

public sealed record BundleSource(string? Session, string? Part, int? Attempt, int? Act, int? Floor);

public readonly record struct BundleCard(string Id, bool Upgraded)
{
    public override string ToString() => ProjectionJson.CardKey(Id, Upgraded);
}

public enum BundleActionType
{
    PlayCard,
    EndTurn
}

public sealed record BundleAction(BundleActionType Type, int HandIndex = -1, string? CardId = null, bool? Upgraded = null, int? TargetCombatId = null)
{
    public const string PlayCardType = "play_card";
    public const string EndTurnType = "end_turn";

    public static BundleAction EndTurn { get; } = new(BundleActionType.EndTurn);

    public static BundleAction Play(int handIndex, string? cardId, bool? upgraded, int? targetCombatId)
        => new(BundleActionType.PlayCard, handIndex, cardId, upgraded, targetCombatId);

    public JsonObject ToJson()
    {
        if (Type == BundleActionType.EndTurn)
        {
            return new JsonObject { ["type"] = EndTurnType };
        }
        var obj = new JsonObject
        {
            ["type"] = PlayCardType,
            ["hand_index"] = HandIndex
        };
        if (CardId != null)
        {
            obj["card_id"] = CardId;
        }
        if (Upgraded is bool upgraded)
        {
            obj["upgraded"] = upgraded;
        }
        obj["target_combat_id"] = TargetCombatId is int t ? JsonValue.Create(t) : null;
        return obj;
    }

    public override string ToString() => Type == BundleActionType.EndTurn
        ? EndTurnType
        : $"{PlayCardType}(hand {HandIndex}{(CardId != null ? " " + ProjectionJson.CardKey(CardId, Upgraded ?? false) : string.Empty)}{(TargetCombatId is int t ? $" -> {t}" : string.Empty)})";
}

public sealed class BundleDecision
{
    public int Index { get; init; }

    public Projection? PreState { get; init; }

    public JsonNode? PreStateJson { get; init; }

    public required BundleAction Action { get; init; }

    // Exporter provenance (`decisions[].oracle`: action_seq, state_seq, settled, ...; sim/specs/diff-bundle-exporter.md §2).
    // Kept verbatim for divergence repros and round trips; never compared.
    public JsonNode? OracleJson { get; init; }

    public bool IsObserved => PreState != null;
}

public sealed class DiffBundle
{
    public const string OutcomeWin = "win";
    public const string OutcomeLoss = "loss";
    public const string OutcomeAbandoned = "abandoned";

    public int BundleVersion { get; init; } = BundleJson.SupportedVersion;

    public BundleSource Source { get; init; } = new(null, null, null, null, null);

    public JsonNode? SourceJson { get; init; }

    public required string GameVersion { get; init; }

    public required string Seeding { get; init; }

    public required string Seed { get; init; }

    public string Character { get; init; } = StartingDecks.IroncladCharacterId;

    public int Ascension { get; init; }

    public required string EncounterId { get; init; }

    public int PlayerHp { get; init; }

    public int PlayerMaxHp { get; init; }

    public IReadOnlyList<BundleCard> Deck { get; init; } = Array.Empty<BundleCard>();

    public IReadOnlyList<string> Relics { get; init; } = Array.Empty<string>();

    public IReadOnlyList<KeyValuePair<string, int>> RngOffsets { get; init; } = Array.Empty<KeyValuePair<string, int>>();

    public IReadOnlyList<BundleDecision> Decisions { get; init; } = Array.Empty<BundleDecision>();

    public required string Outcome { get; init; }

    public int? FinalHp { get; init; }

    // Exporter verdict: top-level `ineligible_reasons`, present only in bundles the exporter wrote under `ineligible/`
    // (`sts2rec export-bundles --include-ineligible`). Empty = eligible (contract v1 default).
    public IReadOnlyList<string> IneligibleReasons { get; init; } = Array.Empty<string>();

    public string? FilePath { get; init; }

    public IReadOnlyCollection<string> IgnoredKeys { get; init; } = Array.Empty<string>();
}

public static class BundleJson
{
    public const int SupportedVersion = 1;

    private static readonly string[] TopLevelKeys =
    {
        "bundle_version", "source", "game_version", "seeding", "seed", "character", "ascension", "encounter_id",
        "player", "deck", "relics", "rng_offsets", "decisions", "outcome", "final_hp", IneligibleReasonsKey
    };

    public const string IneligibleReasonsKey = "ineligible_reasons";

    public const string OracleKey = "oracle";

    public static bool LooksLikeBundle(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("bundle_version", out _);

    public static DiffBundle Parse(string json, string? filePath = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException ex)
        {
            throw new BundleFormatException("$", "invalid JSON: " + ex.Message);
        }
        using (doc)
        {
            return Read(doc.RootElement, filePath);
        }
    }

    public static DiffBundle Read(JsonElement root, string? filePath = null)
    {
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        var r = new JsonReadHelper(ignored);
        r.RequireObject(root, "$");
        r.NoteUnknownKeys(root, "$", TopLevelKeys);
        int version = r.Int(root, "bundle_version", "$");
        if (version != SupportedVersion)
        {
            throw new BundleFormatException("$.bundle_version", $"unsupported bundle version {version} (supported: {SupportedVersion})");
        }

        BundleSource source = new(null, null, null, null, null);
        JsonNode? sourceJson = null;
        if (r.TryProperty(root, "source", out var sourceEl))
        {
            r.RequireObject(sourceEl, "$.source");
            source = new BundleSource(
                OptionalText(sourceEl, "session"),
                OptionalText(sourceEl, "part"),
                r.OptionalInt(sourceEl, "attempt", "$.source"),
                r.OptionalInt(sourceEl, "act", "$.source"),
                r.OptionalInt(sourceEl, "floor", "$.source"));
            sourceJson = JsonNode.Parse(sourceEl.GetRawText());
        }

        string seeding = r.String(root, "seeding", "$");
        if (seeding != RngSetup.SeedingXxh64 && seeding != RngSetup.SeedingLegacyV18)
        {
            throw new BundleFormatException("$.seeding", $"unknown seeding '{seeding}' (expected '{RngSetup.SeedingXxh64}' or '{RngSetup.SeedingLegacyV18}')");
        }

        var playerEl = r.Property(root, "player", "$");
        r.RequireObject(playerEl, "$.player");
        r.NoteUnknownKeys(playerEl, "$.player", "hp", "max_hp");

        var deckEl = r.Property(root, "deck", "$");
        r.RequireArray(deckEl, "$.deck");
        var deck = new List<BundleCard>(deckEl.GetArrayLength());
        int di = 0;
        foreach (var c in deckEl.EnumerateArray())
        {
            string p = $"$.deck[{di}]";
            r.RequireObject(c, p);
            r.NoteUnknownKeys(c, p, "id", "upgraded");
            deck.Add(new BundleCard(r.String(c, "id", p), r.Bool(c, "upgraded", p)));
            di++;
        }

        var relics = r.TryProperty(root, "relics", out _) ? r.StringList(root, "relics", "$") : new List<string>();

        var offsets = new List<KeyValuePair<string, int>>();
        if (r.TryProperty(root, "rng_offsets", out var offEl))
        {
            r.RequireObject(offEl, "$.rng_offsets");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in offEl.EnumerateObject())
            {
                string p = $"$.rng_offsets.{prop.Name}";
                if (!seen.Add(prop.Name))
                {
                    throw new BundleFormatException(p, "duplicate stream name");
                }
                if (!RngSetup.TryParseStream(prop.Name, out _))
                {
                    throw new BundleFormatException(p, $"unknown RNG stream '{prop.Name}'");
                }
                int k = JsonReadHelper.AsInt(prop.Value, p);
                if (k < 0)
                {
                    throw new BundleFormatException(p, $"offset must be non-negative, got {k}");
                }
                offsets.Add(new KeyValuePair<string, int>(prop.Name, k));
            }
        }

        var decisionsEl = r.Property(root, "decisions", "$");
        r.RequireArray(decisionsEl, "$.decisions");
        var decisions = new List<BundleDecision>(decisionsEl.GetArrayLength());
        int i = 0;
        foreach (var d in decisionsEl.EnumerateArray())
        {
            string p = $"$.decisions[{i}]";
            r.RequireObject(d, p);
            r.NoteUnknownKeys(d, p, "index", "pre_state", "action", OracleKey);
            int index = r.Int(d, "index", p);
            if (index != i)
            {
                throw new BundleFormatException(p + ".index", $"decision index {index} does not match its position {i}");
            }
            Projection? pre = null;
            JsonNode? preJson = null;
            if (r.TryProperty(d, "pre_state", out var preEl))
            {
                pre = ProjectionJson.Read(preEl, p + ".pre_state", ignored);
                preJson = JsonNode.Parse(preEl.GetRawText());
            }
            var action = ReadAction(r, r.Property(d, "action", p), p + ".action");
            JsonNode? oracleJson = r.TryProperty(d, OracleKey, out var oracleEl) ? JsonNode.Parse(oracleEl.GetRawText()) : null;
            decisions.Add(new BundleDecision
            {
                Index = index,
                PreState = pre,
                PreStateJson = preJson,
                Action = action,
                OracleJson = oracleJson
            });
            i++;
        }

        string outcome = r.String(root, "outcome", "$");
        if (outcome != DiffBundle.OutcomeWin && outcome != DiffBundle.OutcomeLoss && outcome != DiffBundle.OutcomeAbandoned)
        {
            throw new BundleFormatException("$.outcome", $"unknown outcome '{outcome}'");
        }

        return new DiffBundle
        {
            BundleVersion = version,
            Source = source,
            SourceJson = sourceJson,
            GameVersion = r.String(root, "game_version", "$"),
            Seeding = seeding,
            Seed = r.String(root, "seed", "$"),
            Character = r.String(root, "character", "$"),
            Ascension = r.Int(root, "ascension", "$"),
            EncounterId = r.String(root, "encounter_id", "$"),
            PlayerHp = r.Int(playerEl, "hp", "$.player"),
            PlayerMaxHp = r.Int(playerEl, "max_hp", "$.player"),
            Deck = deck,
            Relics = relics,
            RngOffsets = offsets,
            Decisions = decisions,
            Outcome = outcome,
            FinalHp = r.OptionalInt(root, "final_hp", "$"),
            IneligibleReasons = r.OptionalStringList(root, IneligibleReasonsKey, "$") ?? new List<string>(),
            FilePath = filePath,
            IgnoredKeys = ignored
        };
    }

    public static JsonObject Write(DiffBundle b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var deck = new JsonArray();
        foreach (var c in b.Deck)
        {
            deck.Add(new JsonObject { ["id"] = c.Id, ["upgraded"] = c.Upgraded });
        }
        var relics = new JsonArray();
        foreach (var relic in b.Relics)
        {
            relics.Add(relic);
        }
        var offsets = new JsonObject();
        foreach (var (name, k) in b.RngOffsets)
        {
            offsets[name] = k;
        }
        var decisions = new JsonArray();
        foreach (var d in b.Decisions)
        {
            var decision = new JsonObject
            {
                ["index"] = d.Index,
                ["pre_state"] = d.PreStateJson?.DeepClone() ?? (d.PreState != null ? ProjectionJson.Write(d.PreState) : null),
                ["action"] = d.Action.ToJson()
            };
            if (d.OracleJson != null)
            {
                decision[OracleKey] = d.OracleJson.DeepClone();
            }
            decisions.Add(decision);
        }
        var bundle = new JsonObject
        {
            ["bundle_version"] = b.BundleVersion,
            ["source"] = b.SourceJson?.DeepClone() ?? WriteSource(b.Source),
            ["game_version"] = b.GameVersion,
            ["seeding"] = b.Seeding,
            ["seed"] = b.Seed,
            ["character"] = b.Character,
            ["ascension"] = b.Ascension,
            ["encounter_id"] = b.EncounterId,
            ["player"] = new JsonObject { ["hp"] = b.PlayerHp, ["max_hp"] = b.PlayerMaxHp },
            ["deck"] = deck,
            ["relics"] = relics,
            ["rng_offsets"] = offsets,
            ["decisions"] = decisions,
            ["outcome"] = b.Outcome,
            ["final_hp"] = b.FinalHp is int hp ? JsonValue.Create(hp) : null
        };
        if (b.IneligibleReasons.Count > 0)
        {
            var reasons = new JsonArray();
            foreach (var reason in b.IneligibleReasons)
            {
                reasons.Add(reason);
            }
            bundle[IneligibleReasonsKey] = reasons;
        }
        return bundle;
    }

    public static JsonObject WriteSource(BundleSource s)
    {
        var obj = new JsonObject();
        if (s.Session != null) obj["session"] = s.Session;
        if (s.Part != null) obj["part"] = s.Part;
        if (s.Attempt is int attempt) obj["attempt"] = attempt;
        if (s.Act is int act) obj["act"] = act;
        if (s.Floor is int floor) obj["floor"] = floor;
        return obj;
    }

    private static BundleAction ReadAction(JsonReadHelper r, JsonElement a, string path)
    {
        r.RequireObject(a, path);
        string type = r.String(a, "type", path);
        switch (type)
        {
            case BundleAction.EndTurnType:
                r.NoteUnknownKeys(a, path, "type");
                return BundleAction.EndTurn;
            case BundleAction.PlayCardType:
                r.NoteUnknownKeys(a, path, "type", "hand_index", "card_id", "upgraded", "target_combat_id");
                int handIndex = r.Int(a, "hand_index", path);
                if (handIndex < 0)
                {
                    throw new BundleFormatException(path + ".hand_index", $"must be non-negative, got {handIndex}");
                }
                bool? upgraded = null;
                if (r.TryProperty(a, "upgraded", out _))
                {
                    upgraded = r.Bool(a, "upgraded", path);
                }
                return BundleAction.Play(handIndex, r.OptionalString(a, "card_id", path), upgraded, r.OptionalInt(a, "target_combat_id", path));
            default:
                throw new BundleFormatException(path + ".type", $"unsupported action type '{type}'");
        }
    }

    private static string? OptionalText(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => v.GetRawText()
        };
    }
}
