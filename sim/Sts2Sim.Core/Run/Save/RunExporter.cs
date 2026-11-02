using System.Globalization;
using System.Reflection;

namespace Sts2Sim.Core.Run;

public static class RunExporter
{
    public const ulong SinglePlayerNetId = 1;

    public static SavedRun ToSave(ImportedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ToSave(run.State, run.PreFinishedRoom);
    }

    public static SavedRun ToSave(RunSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (sim.HasPendingChoice)
        {
            throw new InvalidOperationException("A run with a pending prompt has no save point.");
        }
        if (sim.State.Combat is { IsInProgress: true })
        {
            throw new InvalidOperationException("A run inside a combat has no save point.");
        }
        return ToSave(sim.State, PreFinishedRoomOf(sim.State));
    }

    public static SavedRun ToSave(RunState rs, SavedRoom? preFinishedRoom = null)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (rs.Players.Count != 1)
        {
            throw new NotSupportedException("Only single-player runs are exported.");
        }
        var acts = new List<SavedAct>(rs.Acts.Count);
        for (int i = 0; i < rs.Acts.Count; i++)
        {
            acts.Add(ToSavedAct(rs.Acts[i], i == rs.CurrentActIndex ? rs.Map : null));
        }
        return new SavedRun
        {
            SchemaVersion = SavedRun.SupportedSchemaVersion,
            Acts = acts,
            CurrentActIndex = rs.CurrentActIndex,
            EventsSeen = rs.VisitedEventIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            PreFinishedRoom = preFinishedRoom,
            Odds = ToSavedOdds(rs.Odds),
            SharedRelicGrabBag = ToSavedGrabBag(rs.SharedRelicGrabBag),
            Players = new List<SavedPlayer> { ToSavedPlayer(rs, rs.Players[0]) },
            Rng = ToSavedRng(rs.Rng),
            VisitedMapCoords = new List<MapCoord>(rs.VisitedMapCoords),
            MapPointHistory = ToSavedHistory(rs),
            Ascension = rs.Ascension,
            GameMode = SavedRun.StandardGameMode,
            ExtraFields = new SavedExtraRunFields
            {
                StartedWithNeow = rs.ExtraFields.StartedWithNeow,
                TestSubjectKills = rs.ExtraFields.TestSubjectKills,
                FreedRepy = rs.ExtraFields.FreedRepy
            }
        };
    }

    public static SavedRoom? PreFinishedRoomOf(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var room = RoomStack.CurrentRoom(rs);
        switch (room)
        {
            case CombatRoom { IsPreFinished: true } combat:
                return new SavedRoom
                {
                    RoomType = combat.RoomType,
                    EncounterId = combat.Encounter.Id,
                    IsPreFinished = true,
                    GoldProportion = (float)combat.GoldProportion,
                    EncounterState = EncounterCustomState.Save(combat.EncounterExtra),
                    ParentEventId = combat.ParentEventId,
                    ShouldResumeParentEvent = combat.ShouldResumeParentEventAfterCombat
                };
            case EventRoom { IsPreFinished: true } ev:
                return new SavedRoom
                {
                    RoomType = RoomType.Event,
                    EventId = ev.CanonicalEvent.Id,
                    IsPreFinished = true
                };
            default:
                return null;
        }
    }

    public static SavedAct ToSavedAct(ActInstance act, ActMap? map)
    {
        ArgumentNullException.ThrowIfNull(act);
        var rooms = act.Rooms;
        return new SavedAct
        {
            Id = act.Def.Id,
            Rooms = new SavedRoomSet
            {
                EventIds = new List<string>(rooms.events),
                EventsVisited = rooms.eventsVisited,
                NormalEncounterIds = rooms.normalEncounters.Select(e => e.Id).ToList(),
                NormalEncountersVisited = rooms.normalEncountersVisited,
                EliteEncounterIds = rooms.eliteEncounters.Select(e => e.Id).ToList(),
                EliteEncountersVisited = rooms.eliteEncountersVisited,
                BossEncountersVisited = rooms.bossEncountersVisited,
                BossId = rooms.Boss?.Id,
                SecondBossId = rooms.SecondBoss?.Id,
                AncientId = rooms.AncientId
            },
            SavedMap = map == null ? null : ToSavedMap(SerializableActMap.FromActMap(map))
        };
    }

    public static SavedActMapData ToSavedMap(SerializableActMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new SavedActMapData
        {
            Points = map.Points.Select(ToSavedMapPoint).ToList(),
            Boss = ToSavedMapPoint(map.BossPoint),
            SecondBoss = map.SecondBossPoint == null ? null : ToSavedMapPoint(map.SecondBossPoint),
            Start = ToSavedMapPoint(map.StartingPoint),
            StartCoords = map.StartMapPointCoords == null ? new List<MapCoord>() : new List<MapCoord>(map.StartMapPointCoords),
            Width = map.GridWidth,
            Height = map.GridHeight
        };
    }

    private static SavedMapPoint ToSavedMapPoint(SerializableMapPoint p) => new()
    {
        Coord = p.Coord,
        Type = p.PointType,
        CanModify = p.CanBeModified,
        Children = p.ChildCoords == null ? new List<MapCoord>() : new List<MapCoord>(p.ChildCoords)
    };

    private static SavedRunOdds ToSavedOdds(RunOddsSet? odds)
    {
        if (odds == null)
        {
            return new SavedRunOdds();
        }
        var u = odds.UnknownMapPoint;
        return new SavedRunOdds
        {
            UnknownMapPointMonsterOddsValue = u.MonsterOdds,
            UnknownMapPointEliteOddsValue = u.EliteOdds,
            UnknownMapPointTreasureOddsValue = u.TreasureOdds,
            UnknownMapPointShopOddsValue = u.ShopOdds
        };
    }

    public static SavedRelicGrabBag ToSavedGrabBag(RelicGrabBag? bag)
    {
        var lists = new Dictionary<RelicRarity, List<string>>();
        if (bag != null)
        {
            foreach (var rarity in bag.DequeOrder)
            {
                lists[rarity] = new List<string>(bag.GetDequeContents(rarity));
            }
        }
        return new SavedRelicGrabBag { RelicIdLists = lists };
    }

    private static SavedRunRng ToSavedRng(RunRngSet rng)
    {
        var snapshot = rng.ToSnapshot();
        var rngs = new Dictionary<string, RngSnapshot>(StringComparer.Ordinal);
        foreach (var (type, value) in snapshot.Rngs)
        {
            rngs[RunRngSet.StreamName(type)] = value;
        }
        return new SavedRunRng { Seed = snapshot.Seed ?? rng.StringSeed, Rngs = rngs };
    }

    private static SavedPlayerRng ToSavedPlayerRng(PlayerRngSet? rng)
    {
        if (rng == null)
        {
            return new SavedPlayerRng();
        }
        var snapshot = rng.ToSnapshot();
        var rngs = new Dictionary<string, RngSnapshot>(StringComparer.Ordinal);
        foreach (var (type, value) in snapshot.Rngs)
        {
            rngs[PlayerRngSet.StreamName(type)] = value;
        }
        return new SavedPlayerRng { Seed = snapshot.Seed, Rngs = rngs };
    }

    private static List<List<SavedMapPointHistoryEntry>> ToSavedHistory(RunState rs)
    {
        var acts = new List<List<SavedMapPointHistoryEntry>>();
        foreach (var entry in rs.MapPointHistory)
        {
            while (acts.Count <= entry.ActIndex)
            {
                acts.Add(new List<SavedMapPointHistoryEntry>());
            }
            acts[entry.ActIndex].Add(new SavedMapPointHistoryEntry
            {
                MapPointType = entry.MapPointType,
                Rooms = entry.Rooms.Select(r => new SavedMapPointRoom
                {
                    RoomType = r.RoomType,
                    ModelId = r.ModelId,
                    MonsterIds = new List<string>(r.MonsterIds),
                    TurnsTaken = r.TurnsTaken
                }).ToList(),
                GoldGained = entry.GoldGained
            });
        }
        return acts;
    }

    private static SavedPlayer ToSavedPlayer(RunState rs, PlayerState player)
    {
        var potions = new List<SavedPotion>();
        for (int i = 0; i < player.PotionSlots.Length; i++)
        {
            if (player.PotionSlots[i] is { } potion)
            {
                potions.Add(new SavedPotion { Id = potion.Id, SlotIndex = i });
            }
        }
        var odds = player.PlayerOdds;
        return new SavedPlayer
        {
            CharacterId = player.CharacterId,
            CurrentHp = player.CurrentHp,
            MaxHp = player.MaxHp,
            MaxEnergy = player.BaseMaxEnergy,
            MaxPotionSlotCount = player.PotionSlots.Length,
            Gold = player.Gold,
            BaseOrbSlotCount = player.BaseOrbSlotCount,
            NetId = SinglePlayerNetId,
            Deck = player.Deck.Select(ToSavedCard).ToList(),
            Relics = player.Relics.Select(r => new SavedRelic { Id = r.Id, Props = ToSavedProperties(r), FloorAddedToDeck = r.FloorAddedToDeck }).ToList(),
            Potions = potions,
            Rng = ToSavedPlayerRng(player.PlayerRng),
            Odds = odds == null ? new SavedPlayerOdds() : new SavedPlayerOdds
            {
                CardRarityOddsValue = odds.CardRarity.CurrentValue,
                PotionRewardOddsValue = odds.PotionReward.CurrentValue
            },
            RelicGrabBag = ToSavedGrabBag(player.RelicGrabBag),
            ExtraFields = new SavedExtraPlayerFields
            {
                CardShopRemovalsUsed = player.ExtraFields.CardShopRemovalsUsed,
                WongoPoints = player.ExtraFields.WongoPoints
            },
            UnlockState = new SavedUnlockState
            {
                UnlockedEpochs = rs.Unlocks.UnlockedEpochIds().ToList(),
                EncountersSeen = rs.Unlocks.EncountersSeen().ToList(),
                NumberOfRuns = rs.Unlocks.NumberOfRuns
            }
        };
    }

    public static SavedCard ToSavedCard(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var ench = card.Enchantment;
        return new SavedCard
        {
            Id = card.Id,
            CurrentUpgradeLevel = card.UpgradeLevel,
            Enchantment = ench == null ? null : new SavedEnchantment { Id = ench.Id, Amount = ench.Amount, Props = ToSavedProperties(ench) },
            Props = card.Extra == null ? null : ToSavedProperties(card.Extra),
            FloorAddedToDeck = card.FloorAddedToDeck
        };
    }

    public static SavedProperties? ToSavedProperties(object model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var props = new SavedProperties();
        var registered = SavedPropertyRegistry.Ordered(model.GetType()).ToList();
        var members = registered.Select(x => (x.Spec, x.Member))
            .Concat(model.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.CanRead && p.GetCustomAttribute<SavedPropertyAttribute>(true) != null && !registered.Any(r => r.Member.Name == p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => (Spec: new SavedPropertySpec(p.Name), Member: p)))
            .ToList();
        foreach (var (spec, member) in members)
        {
            var value = member.GetValue(model);
            if (!SavedPropertyRegistry.ShouldSave(spec, value))
            {
                continue;
            }
            switch (value)
            {
                case null:
                    break;
                case string id when spec.IsModelId:
                    props.ModelIds.Add(new KeyValuePair<string, string>(member.Name, id));
                    break;
                case Enum e:
                    props.Ints.Add(new KeyValuePair<string, int>(member.Name, Convert.ToInt32(e, CultureInfo.InvariantCulture)));
                    break;
                case int i:
                    props.Ints.Add(new KeyValuePair<string, int>(member.Name, i));
                    break;
                case bool b:
                    props.Bools.Add(new KeyValuePair<string, bool>(member.Name, b));
                    break;
                case string s:
                    props.Strings.Add(new KeyValuePair<string, string>(member.Name, s));
                    break;
                case int[] a:
                    props.IntArrays.Add(new KeyValuePair<string, int[]>(member.Name, a.ToArray()));
                    break;
                default:
                    throw new NotSupportedException($"{model.GetType().Name}.{member.Name}: saved property type {value.GetType().Name} is not exported.");
            }
        }
        return props.IsEmpty ? null : props;
    }

    public static IReadOnlyList<string> Describe(SavedRun save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var lines = new List<string>();
        void L(string path, object? value) => lines.Add(path + "=" + Format(value));
        L("schema_version", save.SchemaVersion);
        L("game_mode", save.GameMode);
        L("daily_time", save.DailyTime);
        L("ascension", save.Ascension);
        L("current_act_index", save.CurrentActIndex);
        L("modifiers", string.Join(",", save.Modifiers.Select(m => m.Id)));
        L("events_seen", string.Join(",", save.EventsSeen.Select(RunSaveReader.StripCategory).OrderBy(x => x, StringComparer.Ordinal)));
        L("visited_map_coords", string.Join(" ", save.VisitedMapCoords.Select(FormatCoord)));
        L("extra_fields", $"{save.ExtraFields.StartedWithNeow},{save.ExtraFields.TestSubjectKills},{save.ExtraFields.FreedRepy}");
        L("odds", string.Join(",", new[] { save.Odds.UnknownMapPointMonsterOddsValue, save.Odds.UnknownMapPointEliteOddsValue, save.Odds.UnknownMapPointTreasureOddsValue, save.Odds.UnknownMapPointShopOddsValue }.Select(FormatFloat)));
        DescribeBag(lines, "shared_relic_grab_bag", save.SharedRelicGrabBag);
        L("rng.seed", save.Rng.Seed);
        foreach (var (name, value) in save.Rng.Rngs.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            L("rng." + name, FormatRng(value));
        }
        for (int a = 0; a < save.Acts.Count; a++)
        {
            var act = save.Acts[a];
            var r = act.Rooms;
            string p = $"acts[{a}]";
            L(p + ".id", act.Id);
            L(p + ".events", $"{string.Join(",", r.EventIds)} @{r.EventsVisited}");
            L(p + ".normal", $"{string.Join(",", r.NormalEncounterIds)} @{r.NormalEncountersVisited}");
            L(p + ".elite", $"{string.Join(",", r.EliteEncounterIds)} @{r.EliteEncountersVisited}");
            L(p + ".boss", $"{r.BossId} {r.SecondBossId} @{r.BossEncountersVisited}");
            L(p + ".ancient", r.AncientId);
            if (act.SavedMap is { } map)
            {
                L(p + ".saved_map", $"{map.Width}x{map.Height} start_coords {string.Join(" ", map.StartCoords.Select(FormatCoord))}");
                foreach (var point in map.Points.OrderBy(x => x.Coord.row).ThenBy(x => x.Coord.col))
                {
                    L(p + ".saved_map.point", FormatPoint(point));
                }
                L(p + ".saved_map.boss", map.Boss == null ? null : FormatPoint(map.Boss));
                L(p + ".saved_map.second_boss", map.SecondBoss == null ? null : FormatPoint(map.SecondBoss));
                L(p + ".saved_map.start", map.Start == null ? null : FormatPoint(map.Start));
            }
            else
            {
                L(p + ".saved_map", null);
            }
        }
        for (int a = 0; a < save.MapPointHistory.Count; a++)
        {
            for (int i = 0; i < save.MapPointHistory[a].Count; i++)
            {
                var entry = save.MapPointHistory[a][i];
                var rooms = string.Join(" | ", entry.Rooms.Select(r => $"{r.RoomType} {r.ModelId} [{string.Join(",", r.MonsterIds)}] t{r.TurnsTaken}"));
                L($"map_point_history[{a}][{i}]", $"{entry.MapPointType} gold {entry.GoldGained ?? 0} {rooms}");
            }
        }
        if (save.PreFinishedRoom is { } room)
        {
            L("pre_finished_room", $"{room.RoomType} {room.EncounterId} {room.EventId} {room.IsPreFinished} {FormatFloat(room.GoldProportion)} {room.ParentEventId} {room.ShouldResumeParentEvent} extra {room.ExtraRewards.Count}");
        }
        else
        {
            L("pre_finished_room", null);
        }
        for (int i = 0; i < save.Players.Count; i++)
        {
            var player = save.Players[i];
            string p = $"players[{i}]";
            L(p + ".character_id", player.CharacterId);
            L(p + ".hp", $"{player.CurrentHp}/{player.MaxHp}");
            L(p + ".max_energy", player.MaxEnergy);
            L(p + ".max_potion_slot_count", player.MaxPotionSlotCount);
            L(p + ".gold", player.Gold);
            L(p + ".base_orb_slot_count", player.BaseOrbSlotCount);
            L(p + ".net_id", player.NetId);
            for (int c = 0; c < player.Deck.Count; c++)
            {
                DescribeCard(lines, $"{p}.deck[{c}]", player.Deck[c]);
            }
            for (int r = 0; r < player.Relics.Count; r++)
            {
                var relic = player.Relics[r];
                L($"{p}.relics[{r}]", relic.Id + DescribeProps(relic.Props));
                L($"{p}.relics[{r}].floor_added_to_deck", relic.FloorAddedToDeck);
            }
            foreach (var potion in player.Potions.OrderBy(x => x.SlotIndex))
            {
                L($"{p}.potions[{potion.SlotIndex}]", potion.Id);
            }
            L(p + ".rng.seed", player.Rng.Seed);
            foreach (var (name, value) in player.Rng.Rngs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                L(p + ".rng." + name, FormatRng(value));
            }
            L(p + ".odds", FormatFloat(player.Odds.CardRarityOddsValue) + "," + FormatFloat(player.Odds.PotionRewardOddsValue));
            DescribeBag(lines, p + ".relic_grab_bag", player.RelicGrabBag);
            L(p + ".extra_fields", $"{player.ExtraFields.CardShopRemovalsUsed},{player.ExtraFields.WongoPoints}");
            L(p + ".unlock_state.unlocked_epochs", string.Join(",", player.UnlockState.UnlockedEpochs.OrderBy(x => x, StringComparer.Ordinal)));
            L(p + ".unlock_state.encounters_seen", string.Join(",", player.UnlockState.EncountersSeen.Select(RunSaveReader.StripCategory).OrderBy(x => x, StringComparer.Ordinal)));
            L(p + ".unlock_state.number_of_runs", player.UnlockState.NumberOfRuns);
        }
        return lines;
    }

    private static void DescribeCard(List<string> lines, string path, SavedCard card)
    {
        var ench = card.Enchantment;
        string e = ench == null ? string.Empty : $" ench {ench.Id} {ench.Amount}{DescribeProps(ench.Props)}";
        lines.Add($"{path}={card.Id}+{card.CurrentUpgradeLevel}{e}{DescribeProps(card.Props)}");
        lines.Add($"{path}.floor_added_to_deck={Format(card.FloorAddedToDeck)}");
    }

    private static void DescribeBag(List<string> lines, string path, SavedRelicGrabBag bag)
    {
        foreach (var (rarity, ids) in bag.RelicIdLists.OrderBy(p => p.Key))
        {
            lines.Add($"{path}.{rarity}={string.Join(",", ids)}");
        }
    }

    private static string DescribeProps(SavedProperties? props)
    {
        if (props == null || props.IsEmpty)
        {
            return string.Empty;
        }
        var items = new List<string>();
        items.AddRange(props.Ints.Select(p => $"{p.Key}:i{p.Value.ToString(CultureInfo.InvariantCulture)}"));
        items.AddRange(props.Bools.Select(p => $"{p.Key}:b{p.Value}"));
        items.AddRange(props.Strings.Concat(props.ModelIds).Select(p => $"{p.Key}:s{p.Value}"));
        items.AddRange(props.IntArrays.Select(p => $"{p.Key}:a{string.Join(",", p.Value)}"));
        items.AddRange(props.Cards.Select(p => $"{p.Key}:card"));
        items.AddRange(props.CardArrays.Select(p => $"{p.Key}:cards"));
        items.Sort(StringComparer.Ordinal);
        return " {" + string.Join(" ", items) + "}";
    }

    private static string FormatPoint(SavedMapPoint p)
        => $"{FormatCoord(p.Coord)} {p.Type} {(p.CanModify ? 1 : 0)} > {string.Join(" ", p.Children.OrderBy(c => c.row).ThenBy(c => c.col).Select(FormatCoord))}";

    private static string FormatCoord(MapCoord c) => c.col.ToString(CultureInfo.InvariantCulture) + "," + c.row.ToString(CultureInfo.InvariantCulture);

    private static string FormatRng(RngSnapshot s) => string.Create(CultureInfo.InvariantCulture, $"{s.Counter} {s.S0} {s.S1} {s.S2} {s.S3}");

    private static string FormatFloat(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8", CultureInfo.InvariantCulture);

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
