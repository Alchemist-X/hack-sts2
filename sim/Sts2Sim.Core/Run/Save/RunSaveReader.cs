using System.Text.Json;

namespace Sts2Sim.Core.Run;

public static class RunSaveReader
{
    public const string CurrentRunFileName = "current_run.save";

    public const string CurrentRunMultiplayerFileName = "current_run_mp.save";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    private static readonly Dictionary<string, RoomType> RoomTypes = SnakeCaseMap<RoomType>();

    private static readonly Dictionary<string, MapPointType> MapPointTypes = SnakeCaseMap<MapPointType>();

    private static readonly Dictionary<string, RelicRarity> RelicRarities = SnakeCaseMap<RelicRarity>();

    public static SavedRun Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A run save is a JSON object.");
        }
        int schema = Int(root, "schema_version");
        if (schema != SavedRun.SupportedSchemaVersion)
        {
            return new SavedRun { SchemaVersion = schema, Json = json };
        }
        return new SavedRun
        {
            SchemaVersion = schema,
            Json = json,
            Acts = Items(root, "acts", ReadAct),
            Modifiers = Items(root, "modifiers", ReadModifier),
            DailyTime = root.TryGetProperty("dailyTime", out var daily) && daily.ValueKind == JsonValueKind.Number ? daily.GetInt64() : null,
            CurrentActIndex = Int(root, "current_act_index"),
            EventsSeen = Strings(root, "events_seen"),
            PreFinishedRoom = Optional(root, "pre_finished_room", ReadRoom),
            Odds = Optional(root, "odds", ReadRunOdds) ?? new SavedRunOdds(),
            SharedRelicGrabBag = Optional(root, "shared_relic_grab_bag", ReadGrabBag) ?? new SavedRelicGrabBag(),
            Players = Items(root, "players", ReadPlayer),
            Rng = Optional(root, "rng", ReadRunRng) ?? new SavedRunRng(),
            VisitedMapCoords = Items(root, "visited_map_coords", ReadCoord),
            MapPointHistory = Items(root, "map_point_history", act => ListOf(act, ReadHistoryEntry)),
            Ascension = Int(root, "ascension"),
            GameMode = String(root, "game_mode") ?? SavedRun.StandardGameMode,
            ExtraFields = Optional(root, "extra_fields", ReadExtraRunFields) ?? new SavedExtraRunFields(),
            PlatformType = String(root, "platform_type")
        };
    }

    public static SavedRun ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(File.ReadAllText(path));
    }

    public static string StripCategory(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        int dot = modelId.IndexOf('.');
        return dot < 0 ? modelId : modelId.Substring(dot + 1);
    }

    private static SavedAct ReadAct(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        Rooms = Optional(e, "rooms", ReadRoomSet) ?? new SavedRoomSet(),
        SavedMap = Optional(e, "saved_map", ReadActMap)
    };

    private static SavedRoomSet ReadRoomSet(JsonElement e) => new()
    {
        EventIds = ModelIds(e, "event_ids"),
        EventsVisited = Int(e, "events_visited"),
        NormalEncounterIds = ModelIds(e, "normal_encounter_ids"),
        NormalEncountersVisited = Int(e, "normal_encounters_visited"),
        EliteEncounterIds = ModelIds(e, "elite_encounter_ids"),
        EliteEncountersVisited = Int(e, "elite_encounters_visited"),
        BossEncountersVisited = Int(e, "boss_encounters_visited"),
        BossId = ModelId(e, "boss_id"),
        SecondBossId = ModelId(e, "second_boss_id"),
        AncientId = ModelId(e, "ancient_id")
    };

    private static SavedActMapData ReadActMap(JsonElement e) => new()
    {
        Points = Items(e, "points", ReadMapPoint),
        Boss = Optional(e, "boss", ReadMapPoint),
        SecondBoss = Optional(e, "second_boss", ReadMapPoint),
        Start = Optional(e, "start", ReadMapPoint),
        StartCoords = Items(e, "start_coords", ReadCoord),
        Width = Int(e, "width"),
        Height = Int(e, "height")
    };

    private static SavedMapPoint ReadMapPoint(JsonElement e) => new()
    {
        Coord = Optional(e, "coord", ReadCoordNullable) ?? default,
        Type = Enum(e, "type", MapPointTypes, MapPointType.Unassigned),
        CanModify = Bool(e, "can_modify"),
        Children = Items(e, "children", ReadCoord)
    };

    private static MapCoord ReadCoord(JsonElement e) => new(Int(e, "col"), Int(e, "row"));

    private static MapCoord? ReadCoordNullable(JsonElement e) => ReadCoord(e);

    private static SavedRoom ReadRoom(JsonElement e)
    {
        var extra = new Dictionary<ulong, List<SavedReward>>();
        if (e.TryGetProperty("extra_rewards", out var rewards) && rewards.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in rewards.EnumerateObject())
            {
                var list = new List<SavedReward>();
                if (p.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in p.Value.EnumerateArray())
                    {
                        list.Add(new SavedReward { Json = r.GetRawText() });
                    }
                }
                extra[ulong.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)] = list;
            }
        }
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty("encounter_state", out var es) && es.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in es.EnumerateObject())
            {
                state[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
            }
        }
        return new SavedRoom
        {
            RoomType = Enum(e, "room_type", RoomTypes, RoomType.Unassigned),
            EncounterId = ModelId(e, "encounter_id"),
            EventId = ModelId(e, "event_id"),
            IsPreFinished = Bool(e, "is_pre_finished"),
            GoldProportion = Float(e, "reward_proportion"),
            ExtraRewards = extra,
            ParentEventId = ModelId(e, "parent_event_id"),
            ShouldResumeParentEvent = !e.TryGetProperty("should_resume_parent_event", out var resume) || resume.ValueKind != JsonValueKind.False,
            EncounterState = state
        };
    }

    private static SavedModifier ReadModifier(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        Props = Optional(e, "props", ReadProps)
    };

    private static SavedRunOdds ReadRunOdds(JsonElement e) => new()
    {
        UnknownMapPointMonsterOddsValue = Float(e, "unknown_map_point_monster_odds_value"),
        UnknownMapPointEliteOddsValue = Float(e, "unknown_map_point_elite_odds_value"),
        UnknownMapPointTreasureOddsValue = Float(e, "unknown_map_point_treasure_odds_value"),
        UnknownMapPointShopOddsValue = Float(e, "unknown_map_point_shop_odds_value")
    };

    private static SavedPlayerOdds ReadPlayerOdds(JsonElement e) => new()
    {
        CardRarityOddsValue = Float(e, "card_rarity_odds_value"),
        PotionRewardOddsValue = Float(e, "potion_reward_odds_value")
    };

    private static SavedRelicGrabBag ReadGrabBag(JsonElement e)
    {
        var lists = new Dictionary<RelicRarity, List<string>>();
        if (e.TryGetProperty("relic_id_lists", out var l) && l.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in l.EnumerateObject())
            {
                if (!RelicRarities.TryGetValue(p.Name, out var rarity))
                {
                    throw new FormatException($"Unknown relic rarity '{p.Name}' in a relic grab bag.");
                }
                lists[rarity] = ListOf(p.Value, x => StripCategory(x.GetString() ?? string.Empty));
            }
        }
        return new SavedRelicGrabBag { RelicIdLists = lists };
    }

    private static SavedRunRng ReadRunRng(JsonElement e) => new()
    {
        Seed = String(e, "seed") ?? string.Empty,
        Rngs = ReadRngs(e)
    };

    private static SavedPlayerRng ReadPlayerRng(JsonElement e) => new()
    {
        Seed = e.TryGetProperty("seed", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetUInt64() : 0uL,
        Rngs = ReadRngs(e)
    };

    private static Dictionary<string, RngSnapshot> ReadRngs(JsonElement e)
    {
        var rngs = new Dictionary<string, RngSnapshot>(StringComparer.Ordinal);
        if (e.TryGetProperty("rngs", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                var v = p.Value;
                rngs[p.Name] = new RngSnapshot(Int(v, "counter"), ULong(v, "s0"), ULong(v, "s1"), ULong(v, "s2"), ULong(v, "s3"));
            }
        }
        return rngs;
    }

    private static SavedExtraRunFields ReadExtraRunFields(JsonElement e) => new()
    {
        StartedWithNeow = Bool(e, "started_with_neow"),
        TestSubjectKills = Int(e, "test_subject_kills"),
        FreedRepy = Bool(e, "freed_repy")
    };

    private static SavedExtraPlayerFields ReadExtraPlayerFields(JsonElement e) => new()
    {
        CardShopRemovalsUsed = Int(e, "card_shop_removals_used"),
        WongoPoints = Int(e, "wongo_points")
    };

    private static SavedPlayer ReadPlayer(JsonElement e) => new()
    {
        CharacterId = StripCategory(String(e, "character_id") ?? string.Empty),
        CurrentHp = Int(e, "current_hp"),
        MaxHp = Int(e, "max_hp"),
        MaxEnergy = Int(e, "max_energy"),
        MaxPotionSlotCount = Int(e, "max_potion_slot_count"),
        Gold = Int(e, "gold"),
        BaseOrbSlotCount = Int(e, "base_orb_slot_count"),
        NetId = ULong(e, "net_id"),
        Deck = Items(e, "deck", ReadCard),
        Relics = Items(e, "relics", ReadRelic),
        Potions = Items(e, "potions", ReadPotion),
        Rng = Optional(e, "rng", ReadPlayerRng) ?? new SavedPlayerRng(),
        Odds = Optional(e, "odds", ReadPlayerOdds) ?? new SavedPlayerOdds(),
        RelicGrabBag = Optional(e, "relic_grab_bag", ReadGrabBag) ?? new SavedRelicGrabBag(),
        ExtraFields = Optional(e, "extra_fields", ReadExtraPlayerFields) ?? new SavedExtraPlayerFields(),
        UnlockState = Optional(e, "unlock_state", ReadUnlockState) ?? new SavedUnlockState()
    };

    private static SavedUnlockState ReadUnlockState(JsonElement e) => new()
    {
        UnlockedEpochs = Strings(e, "unlocked_epochs"),
        EncountersSeen = ModelIds(e, "encounters_seen"),
        NumberOfRuns = Int(e, "number_of_runs")
    };

    private static SavedCard ReadCard(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        CurrentUpgradeLevel = Int(e, "current_upgrade_level"),
        Enchantment = Optional(e, "enchantment", ReadEnchantment),
        Props = Optional(e, "props", ReadProps),
        FloorAddedToDeck = NullableInt(e, "floor_added_to_deck")
    };

    private static SavedEnchantment ReadEnchantment(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        Amount = Int(e, "amount"),
        Props = Optional(e, "props", ReadProps)
    };

    private static SavedRelic ReadRelic(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        Props = Optional(e, "props", ReadProps),
        FloorAddedToDeck = NullableInt(e, "floor_added_to_deck")
    };

    private static SavedPotion ReadPotion(JsonElement e) => new()
    {
        Id = StripCategory(String(e, "id") ?? string.Empty),
        SlotIndex = Int(e, "slot_index")
    };

    private static SavedProperties ReadProps(JsonElement e) => new()
    {
        Ints = Named(e, "ints", v => v.GetInt32()),
        Bools = Named(e, "bools", v => v.GetBoolean()),
        Strings = Named(e, "strings", v => v.GetString() ?? string.Empty),
        IntArrays = Named(e, "int_arrays", v => ListOf(v, x => x.GetInt32()).ToArray()),
        ModelIds = Named(e, "model_ids", v => v.ValueKind == JsonValueKind.String ? StripCategory(v.GetString()!) : v.GetRawText()),
        Cards = Named(e, "cards", ReadCard),
        CardArrays = Named(e, "card_arrays", v => ListOf(v, ReadCard).ToArray())
    };

    private static SavedMapPointHistoryEntry ReadHistoryEntry(JsonElement e)
    {
        int? gold = null;
        if (e.TryGetProperty("player_stats", out var stats) && stats.ValueKind == JsonValueKind.Array && stats.GetArrayLength() > 0)
        {
            gold = NullableInt(stats[0], "gold_gained");
        }
        return new SavedMapPointHistoryEntry
        {
            MapPointType = Enum(e, "map_point_type", MapPointTypes, MapPointType.Unassigned),
            Rooms = Items(e, "rooms", ReadHistoryRoom),
            GoldGained = gold
        };
    }

    private static SavedMapPointRoom ReadHistoryRoom(JsonElement e) => new()
    {
        RoomType = Enum(e, "room_type", RoomTypes, RoomType.Unassigned),
        ModelId = ModelId(e, "model_id"),
        MonsterIds = ModelIds(e, "monster_ids"),
        TurnsTaken = Int(e, "turns_taken")
    };

    private static List<KeyValuePair<string, T>> Named<T>(JsonElement e, string name, Func<JsonElement, T> read)
    {
        var list = new List<KeyValuePair<string, T>>();
        if (e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in a.EnumerateArray())
            {
                list.Add(new KeyValuePair<string, T>(String(x, "name") ?? string.Empty, read(x.GetProperty("value"))));
            }
        }
        return list;
    }

    private static T? Optional<T>(JsonElement e, string name, Func<JsonElement, T> read) where T : class
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? read(v) : null;

    private static T? Optional<T>(JsonElement e, string name, Func<JsonElement, T?> read) where T : struct
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? read(v) : null;

    private static List<T> Items<T>(JsonElement e, string name, Func<JsonElement, T> read)
        => e.TryGetProperty(name, out var v) ? ListOf(v, read) : new List<T>();

    private static List<T> ListOf<T>(JsonElement a, Func<JsonElement, T> read)
    {
        var list = new List<T>();
        if (a.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var x in a.EnumerateArray())
        {
            list.Add(read(x));
        }
        return list;
    }

    private static List<string> Strings(JsonElement e, string name) => Items(e, name, x => x.GetString() ?? string.Empty);

    private static List<string> ModelIds(JsonElement e, string name) => Items(e, name, x => StripCategory(x.GetString() ?? string.Empty));

    private static string? String(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? ModelId(JsonElement e, string name)
    {
        var s = String(e, name);
        return s == null ? null : StripCategory(s);
    }

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static int? NullableInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static ulong ULong(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetUInt64() : 0uL;

    private static float Float(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static T Enum<T>(JsonElement e, string name, Dictionary<string, T> map, T fallback) where T : struct, System.Enum
    {
        var s = String(e, name);
        if (s == null)
        {
            return fallback;
        }
        return map.TryGetValue(s, out var value) ? value : throw new FormatException($"Unknown {typeof(T).Name} '{s}'.");
    }

    private static Dictionary<string, T> SnakeCaseMap<T>() where T : struct, System.Enum
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in System.Enum.GetValues<T>())
        {
            map[DeterministicHash.SnakeCase(value.ToString())] = value;
        }
        return map;
    }
}
