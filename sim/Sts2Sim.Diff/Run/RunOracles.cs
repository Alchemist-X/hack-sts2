using Sts2Sim.Core.Run;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Diff.Run;

public sealed class RunOracleOptions
{
    public UnlockProfile DefaultUnlocks { get; init; } = UnlockProfile.All;

    public bool AnyBuild { get; init; }
}

public static class RunOracleState
{
    public const int OraclePlayerHp = 100;

    public static UnlockProfile UnlocksFor(RunRecord record, RunOracleOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);
        var saved = record.Save?.Players.FirstOrDefault()?.UnlockState;
        if (saved == null)
        {
            return options.DefaultUnlocks;
        }
        return new UnlockProfile(saved.UnlockedEpochs, saved.EncountersSeen, saved.NumberOfRuns);
    }

    public static RunState NewRunState(RunRecord record, UnlockProfile unlocks)
    {
        ArgumentNullException.ThrowIfNull(record);
        var player = new PlayerState(RunImporter.ImportedPlayerCombatId, record.CharacterId, OraclePlayerHp, OraclePlayerHp, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(record.Seed), record.Ascension, unlocks);
        IReadOnlyList<string>? deck = CharacterRegistry.TryGet(record.CharacterId, out var character)
            ? character.StartingDeck
            : string.Equals(record.CharacterId, StartingDecks.IroncladCharacterId, StringComparison.Ordinal) ? StartingDecks.Ironclad : null;
        if (deck != null)
        {
            foreach (var id in StartingDecks.ApplyAscensionEffectsToDeck(deck, record.Ascension))
            {
                if (!CardRegistry.TryGet(id, out var def))
                {
                    continue;
                }
                var card = new CardInstance(def, rs.AllocateCardInstanceId());
                card.AttachTo(player);
                player.Deck.Add(card);
            }
        }
        return rs;
    }

    public static SerializableActMap ToSerializable(SavedActMapData saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return new SerializableActMap
        {
            Points = saved.Points.Select(ToSerializable).ToList(),
            BossPoint = ToSerializable(saved.Boss ?? throw new FormatException("saved_map has no boss point")),
            SecondBossPoint = saved.SecondBoss != null ? ToSerializable(saved.SecondBoss) : null,
            StartingPoint = ToSerializable(saved.Start ?? throw new FormatException("saved_map has no start point")),
            StartMapPointCoords = saved.StartCoords.Count > 0 ? new List<MapCoord>(saved.StartCoords) : null,
            GridWidth = saved.Width,
            GridHeight = saved.Height
        };
    }

    private static SerializableMapPoint ToSerializable(SavedMapPoint p) => new()
    {
        Coord = p.Coord,
        PointType = p.Type,
        CanBeModified = p.CanModify,
        ChildCoords = p.Children.Count > 0 ? new List<MapCoord>(p.Children) : null
    };

    public static SavedActMap? LoadCurrentMap(SavedRun save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.CurrentActIndex < 0 || save.CurrentActIndex >= save.Acts.Count)
        {
            return null;
        }
        var data = save.Acts[save.CurrentActIndex].SavedMap;
        return data == null ? null : new SavedActMap(ToSerializable(data));
    }

    public static string FirstLineDiff(string oracle, string sim)
    {
        var a = oracle.Split('\n');
        var b = sim.Split('\n');
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            string x = i < a.Length ? a[i] : "<end>";
            string y = i < b.Length ? b[i] : "<end>";
            if (!string.Equals(x, y, StringComparison.Ordinal))
            {
                return $"line {i + 1}: oracle '{x}' sim '{y}'";
            }
        }
        return "equal";
    }
}

public static class NeowOracle
{
    public static RunOracleCheck Check(RunRecord record, RunOracleOptions options)
        => RunOracleCheck.Guard(RunOracleNames.Neow, record.Source, () =>
        {
            var floor = record.Floors.Count > 0 && record.Floors[0].Count > 0 ? record.Floors[0][0] : null;
            if (floor == null || floor.PointType != MapPointType.Ancient || floor.Rooms.Count == 0
                || !string.Equals(floor.Rooms[0].ModelId, Neow.EventId, StringComparison.Ordinal) || floor.AncientChoices.Count == 0)
            {
                return RunOracleCheck.Unsupported(RunOracleNames.Neow, record.Source, "no Neow floor with ancient_choice");
            }
            var rs = RunOracleState.NewRunState(record, RunOracleState.UnlocksFor(record, options));
            var ev = EventRegistry.Create(Neow.EventId);
            ev.BeginEvent(rs, rs.Players[0], isPreFinished: false);
            var sim = ev.CurrentOptions.Select(o => o.RelicId ?? o.TextKey).ToList();
            var oracle = floor.AncientChoices.Select(c => c.TextKey).ToList();
            string detail = $"oracle [{string.Join(",", oracle)}] sim [{string.Join(",", sim)}]";
            return sim.SequenceEqual(oracle, StringComparer.Ordinal)
                ? RunOracleCheck.Agree(RunOracleNames.Neow, record.Source, oracle.Count, detail)
                : RunOracleCheck.Diverge(RunOracleNames.Neow, record.Source, oracle.Count, detail);
        });
}

public static class UnknownRollOracle
{
    public static readonly IReadOnlyList<string> RollModifyingRelics = new[] { "JUZU_BRACELET", "GOLDEN_COMPASS" };

    public static readonly IReadOnlyList<string> RollModifyingCards = new[] { "LANTERN_KEY" };

    public static RunOracleCheck Check(RunRecord record, RunOracleOptions options)
        => RunOracleCheck.Guard(RunOracleNames.UnknownRolls, record.Source, () =>
        {
            var blocker = record.FinalRelicIds.Intersect(RollModifyingRelics).Concat(record.FinalDeckIds.Intersect(RollModifyingCards)).FirstOrDefault();
            if (blocker != null)
            {
                return RunOracleCheck.Unsupported(RunOracleNames.UnknownRolls, record.Source, $"{blocker} modifies unknown rolls and its obtain floor is not replayed");
            }
            var rs = RunOracleState.NewRunState(record, RunOracleState.UnlocksFor(record, options));
            var odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
            rs.Odds = odds;
            var save = record.Save;
            ActMap? currentMap = save != null ? RunOracleState.LoadCurrentMap(save) : null;
            int compared = 0;
            int unknownChildren = 0;
            for (int a = 0; a < record.Floors.Count; a++)
            {
                odds.UnknownMapPoint.ResetToBase();
                RunFloor? previous = null;
                var floors = record.Floors[a];
                bool mapForAct = currentMap != null && save != null && a == save.CurrentActIndex;
                for (int i = 0; i < floors.Count; i++)
                {
                    var floor = floors[i];
                    if (floor.PointType == MapPointType.Unknown && floor.Rooms.Count > 0)
                    {
                        var blacklist = new HashSet<RoomType>();
                        if (previous != null && previous.Rooms.Any(r => r.RoomType == RoomType.Shop))
                        {
                            blacklist.Add(RoomType.Shop);
                        }
                        if (mapForAct && i < save!.VisitedMapCoords.Count)
                        {
                            var point = currentMap!.GetPoint(save.VisitedMapCoords[i]);
                            if (point != null && point.Children.Count > 0 && point.Children.All(c => c.PointType == MapPointType.Shop))
                            {
                                blacklist.Add(RoomType.Shop);
                            }
                        }
                        else
                        {
                            unknownChildren++;
                        }
                        var rolled = odds.UnknownMapPoint.Roll(blacklist, rs);
                        compared++;
                        if (rolled != floor.Rooms[0].RoomType)
                        {
                            if (unknownChildren > 0)
                            {
                                return RunOracleCheck.Unsupported(RunOracleNames.UnknownRolls, record.Source,
                                    $"act {a} floor {i + 1}: oracle {floor.Rooms[0].RoomType} sim {rolled} after {unknownChildren} rolls without the map (all-shop-children blacklist unknown), {compared - 1} rolls agreed before");
                            }
                            return RunOracleCheck.Diverge(RunOracleNames.UnknownRolls, record.Source, compared,
                                $"act {a} floor {i + 1}: oracle {floor.Rooms[0].RoomType} sim {rolled}");
                        }
                    }
                    previous = floor;
                }
            }
            string note = unknownChildren > 0 ? $"{unknownChildren} rolls without the map (all-shop-children blacklist not applied)" : string.Empty;
            if (save != null)
            {
                if (save.Rng.Rngs.TryGetValue(RunRngSet.StreamName(RunRngType.UnknownMapPoint), out var snap) && snap.Counter != rs.Rng.UnknownMapPoint.Counter)
                {
                    return RunOracleCheck.Diverge(RunOracleNames.UnknownRolls, record.Source, compared,
                        $"unknown_map_point counter oracle {snap.Counter} sim {rs.Rng.UnknownMapPoint.Counter}");
                }
                var u = odds.UnknownMapPoint;
                var o = save.Odds;
                if (u.MonsterOdds != o.UnknownMapPointMonsterOddsValue || u.EliteOdds != o.UnknownMapPointEliteOddsValue
                    || u.TreasureOdds != o.UnknownMapPointTreasureOddsValue || u.ShopOdds != o.UnknownMapPointShopOddsValue)
                {
                    return RunOracleCheck.Diverge(RunOracleNames.UnknownRolls, record.Source, compared, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"odds oracle {o.UnknownMapPointMonsterOddsValue:R}/{o.UnknownMapPointEliteOddsValue:R}/{o.UnknownMapPointTreasureOddsValue:R}/{o.UnknownMapPointShopOddsValue:R} sim {u.MonsterOdds:R}/{u.EliteOdds:R}/{u.TreasureOdds:R}/{u.ShopOdds:R}"));
                }
            }
            return RunOracleCheck.Agree(RunOracleNames.UnknownRolls, record.Source, compared, note);
        });
}

public static class RoomPullOracle
{
    public sealed record ActRooms(RoomSet Rooms, string? MissingEncounter, SavedRoomSet? Expected);

    public static IReadOnlyList<RunOracleCheck> Check(RunRecord record, RunOracleOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        List<ActRooms>? acts = null;
        string? unsupported = null;
        try
        {
            acts = record.Save != null ? FromSave(record.Save) : FromGeneration(record, options);
        }
        catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or KeyNotFoundException)
        {
            unsupported = "room sets: " + ex.Message;
        }
        if (acts == null)
        {
            return new[]
            {
                RunOracleCheck.Unsupported(RunOracleNames.EncounterPulls, record.Source, unsupported ?? "no room sets"),
                RunOracleCheck.Unsupported(RunOracleNames.EventPulls, record.Source, unsupported ?? "no room sets")
            };
        }
        var rs = RunOracleState.NewRunState(record, RunOracleState.UnlocksFor(record, options));
        return new[]
        {
            RunOracleCheck.Guard(RunOracleNames.EncounterPulls, record.Source, () => CheckEncounters(record, acts)),
            RunOracleCheck.Guard(RunOracleNames.EventPulls, record.Source, () => CheckEvents(record, acts, rs))
        };
    }

    public static List<ActRooms> FromSave(SavedRun save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var list = new List<ActRooms>(save.Acts.Count);
        foreach (var act in save.Acts)
        {
            var s = act.Rooms;
            var rooms = new RoomSet { AncientId = s.AncientId };
            rooms.events.AddRange(s.EventIds);
            string? missing = null;
            foreach (var id in s.NormalEncounterIds)
            {
                missing ??= Add(rooms.normalEncounters, id);
            }
            foreach (var id in s.EliteEncounterIds)
            {
                missing ??= Add(rooms.eliteEncounters, id);
            }
            if (s.BossId != null)
            {
                rooms.Boss = EncounterRegistry.TryGet(s.BossId, out var boss) ? boss : null;
                missing ??= rooms.Boss == null ? s.BossId : null;
            }
            if (s.SecondBossId != null)
            {
                rooms.SecondBoss = EncounterRegistry.TryGet(s.SecondBossId, out var second) ? second : null;
                missing ??= rooms.SecondBoss == null ? s.SecondBossId : null;
            }
            list.Add(new ActRooms(rooms, missing, s));
        }
        return list;
    }

    public static List<ActRooms> FromGeneration(RunRecord record, RunOracleOptions options)
    {
        var rs = RunStartOracle.Generate(record, RunOracleState.UnlocksFor(record, options));
        return rs.Acts.Select(a =>
        {
            var rooms = a.Rooms.DeepClone();
            rooms.normalEncountersVisited = 0;
            rooms.eliteEncountersVisited = 0;
            rooms.bossEncountersVisited = 0;
            rooms.eventsVisited = 0;
            return new ActRooms(rooms, null, null);
        }).ToList();
    }

    private static string? Add(List<EncounterDef> list, string id)
    {
        if (EncounterRegistry.TryGet(id, out var def))
        {
            list.Add(def);
            return null;
        }
        return id;
    }

    private static RunOracleCheck CheckEncounters(RunRecord record, List<ActRooms> acts)
    {
        int compared = 0;
        int n = Math.Min(acts.Count, record.Floors.Count);
        for (int a = 0; a < n; a++)
        {
            var act = acts[a];
            if (act.MissingEncounter != null)
            {
                return RunOracleCheck.Unsupported(RunOracleNames.EncounterPulls, record.Source, $"act {a}: encounter {act.MissingEncounter} in the room set is not ported");
            }
            var rooms = act.Rooms.DeepClone();
            var floors = record.Floors[a];
            for (int i = 0; i < floors.Count; i++)
            {
                if (floors[i].Rooms.Count == 0)
                {
                    continue;
                }
                var room = floors[i].Rooms[0];
                if (room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
                {
                    var expected = room.RoomType switch
                    {
                        RoomType.Monster => rooms.NextNormalEncounter,
                        RoomType.Elite => rooms.NextEliteEncounter,
                        _ => rooms.NextBossEncounter
                    };
                    compared++;
                    if (!string.Equals(expected.Id, room.ModelId, StringComparison.Ordinal))
                    {
                        return RunOracleCheck.Diverge(RunOracleNames.EncounterPulls, record.Source, compared, $"act {a} floor {i + 1} {room.RoomType}: oracle {room.ModelId} sim {expected.Id}");
                    }
                }
                rooms.MarkVisited(room.RoomType);
            }
            if (act.Expected is { } s)
            {
                if (rooms.normalEncountersVisited != s.NormalEncountersVisited || rooms.eliteEncountersVisited != s.EliteEncountersVisited || rooms.bossEncountersVisited != s.BossEncountersVisited)
                {
                    return RunOracleCheck.Diverge(RunOracleNames.EncounterPulls, record.Source, compared,
                        $"act {a} visited counters normal/elite/boss oracle {s.NormalEncountersVisited}/{s.EliteEncountersVisited}/{s.BossEncountersVisited} sim {rooms.normalEncountersVisited}/{rooms.eliteEncountersVisited}/{rooms.bossEncountersVisited}");
                }
            }
        }
        return RunOracleCheck.Agree(RunOracleNames.EncounterPulls, record.Source, compared);
    }

    private static RunOracleCheck CheckEvents(RunRecord record, List<ActRooms> acts, RunState rs)
    {
        int compared = 0;
        int n = Math.Min(acts.Count, record.Floors.Count);
        for (int a = 0; a < n; a++)
        {
            var rooms = acts[a].Rooms.DeepClone();
            var floors = record.Floors[a];
            rs.CurrentActIndex = a;
            for (int i = 0; i < floors.Count; i++)
            {
                if (floors[i].Rooms.Count == 0)
                {
                    rs.MapPointHistory.Add(new MapPointHistoryEntry(floors[i].PointType) { ActIndex = a });
                    continue;
                }
                var room = floors[i].Rooms[0];
                if (room.RoomType == RoomType.Event)
                {
                    string expected;
                    if (floors[i].PointType == MapPointType.Ancient)
                    {
                        expected = rooms.AncientId ?? "<none>";
                    }
                    else
                    {
                        var considered = Considered(rooms, rs, room.ModelId);
                        rooms.EnsureNextEventIsValid(rs);
                        expected = rooms.events.Count > 0 ? rooms.NextEventId : "<none>";
                        if (!string.Equals(expected, room.ModelId, StringComparison.Ordinal))
                        {
                            var unported = considered.Concat(new[] { expected }).FirstOrDefault(id => !EventRegistry.Models.Contains(id) && id != room.ModelId);
                            if (unported != null)
                            {
                                return RunOracleCheck.Unsupported(RunOracleNames.EventPulls, record.Source,
                                    $"act {a} floor {i + 1}: event {unported} IsAllowed is not ported (oracle {room.ModelId} sim {expected}, {compared} pulls agreed before)");
                            }
                        }
                        rs.VisitedEventIds.Add(expected);
                    }
                    compared++;
                    if (!string.Equals(expected, room.ModelId, StringComparison.Ordinal))
                    {
                        return RunOracleCheck.Diverge(RunOracleNames.EventPulls, record.Source, compared, $"act {a} floor {i + 1} {floors[i].PointType}: oracle {room.ModelId} sim {expected}");
                    }
                }
                rooms.MarkVisited(room.RoomType);
                rs.MapPointHistory.Add(new MapPointHistoryEntry(floors[i].PointType) { ActIndex = a });
            }
            if (acts[a].Expected is { } s && rooms.eventsVisited != s.EventsVisited)
            {
                return RunOracleCheck.Diverge(RunOracleNames.EventPulls, record.Source, compared, $"act {a} events_visited oracle {s.EventsVisited} sim {rooms.eventsVisited}");
            }
        }
        return RunOracleCheck.Agree(RunOracleNames.EventPulls, record.Source, compared, "event IsAllowed evaluated on an oracle run state (floor and act replayed; no gold/hp/deck/potion replay)");
    }

    private static List<string> Considered(RoomSet rooms, RunState rs, string? oracleId)
    {
        var ids = new List<string>();
        int count = rooms.events.Count;
        for (int k = 0; k < count; k++)
        {
            var id = rooms.events[(rooms.eventsVisited + k) % count];
            if (rs.VisitedEventIds.Contains(id))
            {
                continue;
            }
            if (string.Equals(id, oracleId, StringComparison.Ordinal))
            {
                break;
            }
            ids.Add(id);
        }
        return ids;
    }
}

public static class MapOracle
{
    public static IReadOnlyList<RunOracleCheck> Check(RunRecord record, RunOracleOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        var save = record.Save;
        if (save == null)
        {
            return new[]
            {
                RunOracleCheck.Unsupported(RunOracleNames.MapSaved, record.Source, ".run files carry no map"),
                RunOracleCheck.Unsupported(RunOracleNames.MapGenerated, record.Source, ".run files carry no map"),
                RunOracleCheck.Unsupported(RunOracleNames.MapPath, record.Source, ".run files carry no map")
            };
        }
        return new[]
        {
            RunOracleCheck.Guard(RunOracleNames.MapSaved, record.Source, () => CheckSaved(record, save)),
            RunOracleCheck.Guard(RunOracleNames.MapGenerated, record.Source, () => CheckGenerated(record, save, options)),
            RunOracleCheck.Guard(RunOracleNames.MapPath, record.Source, () => CheckPath(record, save, options))
        };
    }

    private static SavedActMapData? CurrentData(SavedRun save)
        => save.CurrentActIndex >= 0 && save.CurrentActIndex < save.Acts.Count ? save.Acts[save.CurrentActIndex].SavedMap : null;

    private static RunOracleCheck CheckSaved(RunRecord record, SavedRun save)
    {
        var data = CurrentData(save);
        if (data == null)
        {
            return RunOracleCheck.Unsupported(RunOracleNames.MapSaved, record.Source, "current act has no saved_map");
        }
        var serial = RunOracleState.ToSerializable(data);
        string oracle = serial.ToCompactString(canModifyAfterJsonRoundTrip: true);
        string sim = SerializableActMap.FromActMap(new SavedActMap(serial)).ToCompactString(canModifyAfterJsonRoundTrip: true);
        return string.Equals(oracle, sim, StringComparison.Ordinal)
            ? RunOracleCheck.Agree(RunOracleNames.MapSaved, record.Source, data.Points.Count + 2)
            : RunOracleCheck.Diverge(RunOracleNames.MapSaved, record.Source, data.Points.Count + 2, RunOracleState.FirstLineDiff(oracle, sim));
    }

    private static RunOracleCheck CheckGenerated(RunRecord record, SavedRun save, RunOracleOptions options)
    {
        var data = CurrentData(save);
        if (data == null)
        {
            return RunOracleCheck.Unsupported(RunOracleNames.MapGenerated, record.Source, "current act has no saved_map");
        }
        foreach (var act in save.Acts)
        {
            if (!ActRegistry.TryGet(act.Id, out _))
            {
                return RunOracleCheck.Unsupported(RunOracleNames.MapGenerated, record.Source, $"act {act.Id} is not ported (W3 content)");
            }
            var r = act.Rooms;
            var missing = r.NormalEncounterIds.Concat(r.EliteEncounterIds).Concat(new[] { r.BossId, r.SecondBossId }.OfType<string>()).FirstOrDefault(e => !EncounterRegistry.Defs.Contains(e));
            if (missing != null)
            {
                return RunOracleCheck.Unsupported(RunOracleNames.MapGenerated, record.Source, $"encounter {missing} is not ported (W3 content)");
            }
        }
        var rs = RunOracleState.NewRunState(record, RunOracleState.UnlocksFor(record, options));
        foreach (var act in save.Acts)
        {
            rs.Acts.Add(RunImporter.ToActInstance(act));
        }
        rs.CurrentActIndex = save.CurrentActIndex;
        var map = rs.Act.Def.CreateMap(rs, replaceTreasureWithElites: false);
        string oracle = RunOracleState.ToSerializable(data).ToCompactString(canModifyAfterJsonRoundTrip: true);
        string sim = SerializableActMap.FromActMap(map).ToCompactString(canModifyAfterJsonRoundTrip: true);
        return string.Equals(oracle, sim, StringComparison.Ordinal)
            ? RunOracleCheck.Agree(RunOracleNames.MapGenerated, record.Source, data.Points.Count + 2)
            : RunOracleCheck.Diverge(RunOracleNames.MapGenerated, record.Source, data.Points.Count + 2, RunOracleState.FirstLineDiff(oracle, sim));
    }

    private static RunOracleCheck CheckPath(RunRecord record, SavedRun save, RunOracleOptions options)
    {
        var map = RunOracleState.LoadCurrentMap(save);
        if (map == null)
        {
            return RunOracleCheck.Unsupported(RunOracleNames.MapPath, record.Source, "current act has no saved_map");
        }
        if (record.FinalRelicIds.Contains("WINGED_BOOTS", StringComparer.Ordinal))
        {
            return RunOracleCheck.Unsupported(RunOracleNames.MapPath, record.Source, "WINGED_BOOTS free travel is not replayed");
        }
        var rs = RunOracleState.NewRunState(record, RunOracleState.UnlocksFor(record, options));
        rs.Map = map;
        var floors = save.CurrentActIndex < record.Floors.Count ? record.Floors[save.CurrentActIndex] : new List<RunFloor>();
        int compared = 0;
        for (int i = 0; i < save.VisitedMapCoords.Count; i++)
        {
            var coord = save.VisitedMapCoords[i];
            var legal = MapTravel.GetLegalMapPoints(rs);
            var point = legal.FirstOrDefault(p => p.coord == coord);
            compared++;
            if (point == null)
            {
                return RunOracleCheck.Diverge(RunOracleNames.MapPath, record.Source, compared,
                    $"step {i}: {coord.col},{coord.row} not in sim legal set [{string.Join(" ", legal.Select(p => $"{p.coord.col},{p.coord.row}"))}]");
            }
            if (i < floors.Count && floors[i].PointType != point.PointType)
            {
                return RunOracleCheck.Diverge(RunOracleNames.MapPath, record.Source, compared, $"step {i}: history type {floors[i].PointType} map type {point.PointType}");
            }
            rs.VisitedMapCoords.Add(coord);
        }
        return RunOracleCheck.Agree(RunOracleNames.MapPath, record.Source, compared);
    }
}

public static class RunStartOracle
{
    public static RunState Generate(RunRecord record, UnlockProfile unlocks)
    {
        ArgumentNullException.ThrowIfNull(record);
        foreach (var id in record.ActIds)
        {
            if (!ActRegistry.TryGet(id, out var act))
            {
                throw new NotSupportedException($"act {id} is not ported (W3 content)");
            }
            var unported = act.DeclaredEncounterIds.Where(e => !EncounterRegistry.Defs.Contains(e)).ToArray();
            if (unported.Length > 0)
            {
                throw new NotSupportedException($"act {id}: encounters {string.Join(",", unported)} are not ported (W3 content)");
            }
        }
        RunLoop.GetCharacterStart(record.CharacterId);
        var config = new RunConfig(record.Seed, record.CharacterId, record.Ascension)
        {
            ActIds = record.ActIds.ToArray(),
            Unlocks = unlocks,
            Content = ContentFilter.All
        };
        return RunLoop.CreateForNewRun(config);
    }

    public static RunOracleCheck Check(RunRecord record, RunOracleOptions options)
        => RunOracleCheck.Guard(RunOracleNames.RunStart, record.Source, () =>
        {
            var save = record.Save;
            if (save == null)
            {
                return RunOracleCheck.Unsupported(RunOracleNames.RunStart, record.Source, "run-start oracle needs a save");
            }
            var rs = Generate(record, RunOracleState.UnlocksFor(record, options));
            int compared = 0;
            for (int a = 0; a < save.Acts.Count && a < rs.Acts.Count; a++)
            {
                var s = save.Acts[a].Rooms;
                var r = rs.Acts[a].Rooms;
                var pairs = new (string Name, string Oracle, string Sim)[]
                {
                    ("events", string.Join(",", s.EventIds), string.Join(",", r.events)),
                    ("normal", string.Join(",", s.NormalEncounterIds), string.Join(",", r.normalEncounters.Select(e => e.Id))),
                    ("elite", string.Join(",", s.EliteEncounterIds), string.Join(",", r.eliteEncounters.Select(e => e.Id))),
                    ("boss", s.BossId ?? "", r.Boss?.Id ?? ""),
                    ("second_boss", s.SecondBossId ?? "", r.SecondBoss?.Id ?? ""),
                    ("ancient", s.AncientId ?? "", r.AncientId ?? "")
                };
                foreach (var (name, oracle, sim) in pairs)
                {
                    compared++;
                    if (!string.Equals(oracle, sim, StringComparison.Ordinal))
                    {
                        return RunOracleCheck.Diverge(RunOracleNames.RunStart, record.Source, compared, $"act {a} {name}: oracle [{oracle}] sim [{sim}]");
                    }
                }
            }
            string upFront = RunRngSet.StreamName(RunRngType.UpFront);
            if (save.Rng.Rngs.TryGetValue(upFront, out var snap))
            {
                compared++;
                if (snap.Counter != rs.Rng.UpFront.Counter)
                {
                    return RunOracleCheck.Diverge(RunOracleNames.RunStart, record.Source, compared, $"up_front counter oracle {snap.Counter} sim {rs.Rng.UpFront.Counter}");
                }
            }
            var bags = new (string Name, SavedRelicGrabBag Oracle, RelicGrabBag? Sim)[]
            {
                ("shared", save.SharedRelicGrabBag, rs.SharedRelicGrabBag),
                ("player", save.Players[0].RelicGrabBag, rs.Players[0].RelicGrabBag)
            };
            foreach (var (name, oracle, sim) in bags)
            {
                foreach (var (rarity, ids) in oracle.RelicIdLists)
                {
                    compared++;
                    var simIds = sim?.GetDequeContents(rarity) ?? Array.Empty<string>();
                    if (!IsSubsequence(ids, simIds))
                    {
                        return RunOracleCheck.Diverge(RunOracleNames.RunStart, record.Source, compared,
                            $"{name} grab bag {rarity}: oracle [{string.Join(",", ids)}] is not an ordered subset of sim [{string.Join(",", simIds)}]");
                    }
                }
            }
            return RunOracleCheck.Agree(RunOracleNames.RunStart, record.Source, compared, "grab bags checked as ordered subsets (pulls after run start)");
        });

    public static bool IsSubsequence(IReadOnlyList<string> sub, IReadOnlyList<string> full)
    {
        int j = 0;
        for (int i = 0; i < full.Count && j < sub.Count; i++)
        {
            if (string.Equals(full[i], sub[j], StringComparison.Ordinal))
            {
                j++;
            }
        }
        return j == sub.Count;
    }
}
