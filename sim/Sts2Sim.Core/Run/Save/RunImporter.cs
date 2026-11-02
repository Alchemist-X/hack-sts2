using System.Reflection;

namespace Sts2Sim.Core.Run;

public static class RunImporter
{
    // The game attaches the player first to a fresh CombatState (_nextCreatureId = 0), as the seed path does
    // (Combat/CombatState.cs:703-706, :726-731); monster Rng seeds add the combat id (:721).
    public const int ImportedPlayerCombatId = CombatLoop.PlayerCombatId;

    public static RunImportResult FromSave(SavedRun save, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(options);
        var reasons = CheckSupported(save, options);
        SavePoint? entry = save.SchemaVersion == SavedRun.SupportedSchemaVersion ? DetectEntryPoint(save) : null;
        if (reasons.Count > 0)
        {
            return new RunImportResult(null, entry, reasons);
        }
        return new RunImportResult(RunSim.FromSave(save, options), entry, reasons);
    }

    public static SavePoint DetectEntryPoint(SavedRun save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var room = save.PreFinishedRoom;
        if (room != null)
        {
            switch (room.RoomType)
            {
                case RoomType.Monster:
                case RoomType.Elite:
                case RoomType.Boss:
                    return SavePoint.PostCombatRewards;
                case RoomType.Event:
                    return SavePoint.AncientFinished;
                default:
                    throw new ArgumentOutOfRangeException(nameof(save), room.RoomType, "A pre-finished room is a combat or an event room.");
            }
        }
        return save.VisitedMapCoords.Count > 0 ? SavePoint.EnteringMapPoint : SavePoint.NoVisitedCoord;
    }

    public static IReadOnlyList<string> CheckSupported(SavedRun save, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(options);
        var reasons = new List<string>();
        void Add(string reason)
        {
            if (!reasons.Contains(reason, StringComparer.Ordinal))
            {
                reasons.Add(reason);
            }
        }
        if (save.SchemaVersion != SavedRun.SupportedSchemaVersion)
        {
            Add($"schema_version {save.SchemaVersion} is not supported (only {SavedRun.SupportedSchemaVersion}; older saves need the game's migrations)");
            return reasons;
        }
        if (!string.Equals(save.GameMode, SavedRun.StandardGameMode, StringComparison.Ordinal))
        {
            Add($"game_mode {save.GameMode} is not standard");
        }
        if (save.DailyTime != null)
        {
            Add("daily runs are not supported");
        }
        foreach (var modifier in save.Modifiers)
        {
            Add($"modifier {modifier.Id} is not supported");
        }
        if (save.Players.Count != 1)
        {
            Add($"{save.Players.Count} players: only single-player saves are supported");
        }
        foreach (var name in save.Rng.Rngs.Keys)
        {
            if (!TryRunStream(name, out _))
            {
                Add($"run rng stream {name} is unknown");
            }
        }
        if (save.CurrentActIndex < 0 || save.CurrentActIndex >= save.Acts.Count)
        {
            Add($"current_act_index {save.CurrentActIndex} is outside the {save.Acts.Count} acts");
        }
        foreach (var act in save.Acts)
        {
            if (!ActRegistry.Defs.Contains(act.Id))
            {
                Add($"act {act.Id} is not ported");
            }
            var rooms = act.Rooms;
            foreach (var id in rooms.NormalEncounterIds.Concat(rooms.EliteEncounterIds))
            {
                CheckEncounter(id, Add);
            }
            if (rooms.BossId != null)
            {
                CheckEncounter(rooms.BossId, Add);
            }
            if (rooms.SecondBossId != null)
            {
                CheckEncounter(rooms.SecondBossId, Add);
            }
            if (rooms.AncientId != null)
            {
                CheckEvent(rooms.AncientId, Add);
            }
        }
        var pre = save.PreFinishedRoom;
        if (pre != null)
        {
            if (pre.RoomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss or RoomType.Event))
            {
                Add($"pre_finished_room room_type {pre.RoomType} cannot be resumed");
            }
            if (pre.EncounterId != null)
            {
                CheckEncounter(pre.EncounterId, Add);
            }
            if (pre.EventId != null)
            {
                CheckEvent(pre.EventId, Add);
            }
            if (pre.ParentEventId != null)
            {
                CheckEvent(pre.ParentEventId, Add);
            }
            if (pre.ExtraRewards.Count > 0)
            {
                Add("pre_finished_room extra_rewards are not supported");
            }
        }
        foreach (var player in save.Players)
        {
            CheckPlayer(player, Add);
        }
        return reasons;
    }

    public static RunState ImportCore(SavedRun save, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(options);
        RequireSchema(save);
        var saved = save.Players.Count == 1 ? save.Players[0] : throw new NotSupportedException("Only single-player saves are imported.");
        var player = CreatePlayer(saved);
        var rng = RunRngSet.FromSnapshot(ToSnapshot(save.Rng));
        var unlocks = new UnlockProfile(saved.UnlockState.UnlockedEpochs, saved.UnlockState.EncountersSeen, saved.UnlockState.NumberOfRuns);
        var rs = RunState.CreateStandalone(player, rng, save.Ascension, unlocks);
        LoadInventory(rs, player, saved);
        rs.Config = new RunConfig(save.Rng.Seed, player.CharacterId, save.Ascension)
        {
            ActIds = save.Acts.Select(a => a.Id).ToArray(),
            Act1 = save.Acts.Count > 0 ? save.Acts[0].Id : RunConfig.RandomAct1,
            Unlocks = unlocks,
            Progress = options.Progress,
            Content = options.Content
        };
        rs.CurrentActIndex = save.CurrentActIndex;
        rs.VisitedMapCoords.AddRange(save.VisitedMapCoords);
        rs.VisitedEventIds.UnionWith(save.EventsSeen.Select(RunSaveReader.StripCategory));
        for (int actIndex = 0; actIndex < save.MapPointHistory.Count; actIndex++)
        {
            foreach (var entry in save.MapPointHistory[actIndex])
            {
                rs.MapPointHistory.Add(ToHistoryEntry(entry, actIndex));
            }
        }
        rs.ExtraFields.StartedWithNeow = save.ExtraFields.StartedWithNeow;
        rs.ExtraFields.TestSubjectKills = save.ExtraFields.TestSubjectKills;
        rs.ExtraFields.FreedRepy = save.ExtraFields.FreedRepy;
        return rs;
    }

    public static ImportedRun ImportState(SavedRun save, ImportOptions options)
    {
        var rs = ImportCore(save, options);
        foreach (var act in save.Acts)
        {
            rs.Acts.Add(ToActInstance(act));
        }
        var odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
        LoadUnknownMapPointOdds(odds.UnknownMapPoint, save.Odds);
        rs.Odds = odds;
        rs.SharedRelicGrabBag = LoadRelicGrabBag(save.SharedRelicGrabBag, refreshAllowed: false);
        var player = rs.Players[0];
        var saved = save.Players[0];
        player.RelicGrabBag = LoadRelicGrabBag(saved.RelicGrabBag, refreshAllowed: false);
        var playerOdds = new PlayerOddsSet(player.PlayerRng!, save.Ascension);
        playerOdds.CardRarity.OverrideCurrentValue(saved.Odds.CardRarityOddsValue);
        playerOdds.PotionReward.OverrideCurrentValue(saved.Odds.PotionRewardOddsValue);
        player.PlayerOdds = playerOdds;
        var map = save.Acts[save.CurrentActIndex].SavedMap;
        if (map != null)
        {
            rs.Map = LoadSavedMap(rs, map);
        }
        return new ImportedRun(rs, DetectEntryPoint(save), save.PreFinishedRoom);
    }

    public static PlayerState CreatePlayer(SavedPlayer saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var player = new PlayerState(ImportedPlayerCombatId, saved.CharacterId, saved.CurrentHp, saved.MaxHp, saved.MaxEnergy)
        {
            Gold = saved.Gold,
            BaseOrbSlotCount = saved.BaseOrbSlotCount,
            PotionSlots = new PotionModel?[saved.MaxPotionSlotCount],
            AscensionEffectsApplied = true,
            PlayerRng = PlayerRngSet.FromSnapshot(ToSnapshot(saved.Rng))
        };
        player.ExtraFields.CardShopRemovalsUsed = saved.ExtraFields.CardShopRemovalsUsed;
        player.ExtraFields.WongoPoints = saved.ExtraFields.WongoPoints;
        return player;
    }

    public static void LoadInventory(RunState rs, PlayerState player, SavedPlayer saved)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(saved);
        if (player.Deck.Count > 0 || player.Relics.Count > 0 || player.Potions.Any())
        {
            throw new InvalidOperationException("Inventory is already populated.");
        }
        foreach (var card in saved.Deck)
        {
            player.Deck.Add(CreateCard(rs, player, card));
        }
        foreach (var potion in saved.Potions)
        {
            var model = PotionRegistry.Create(potion.Id);
            var result = player.AddPotionInternal(model, potion.SlotIndex);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Potion {potion.Id} does not fit slot {potion.SlotIndex}.");
            }
        }
        foreach (var relic in saved.Relics)
        {
            var model = RelicRegistry.Create(relic.Id);
            if (relic.Props != null)
            {
                FillProperties(model, relic.Props);
            }
            if (relic.FloorAddedToDeck is int floor)
            {
                model.FloorAddedToDeck = floor;
            }
            player.AddRelicInternal(model);
        }
    }

    public static CardInstance CreateCard(RunState rs, PlayerState owner, SavedCard saved)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(saved);
        var def = CardRegistry.Get(saved.Id);
        var card = new CardInstance(def, rs.AllocateCardInstanceId());
        card.AttachTo(owner);
        card.FloorAddedToDeck = saved.FloorAddedToDeck;
        if (saved.Props != null)
        {
            FillProperties(card.Extra ?? throw new NotSupportedException($"Card {saved.Id} has saved properties but no extra state."), saved.Props);
        }
        if (saved.Enchantment != null)
        {
            var enchantment = EnchantmentRegistry.Create(saved.Enchantment.Id);
            if (saved.Enchantment.Props != null)
            {
                FillProperties(enchantment, saved.Enchantment.Props);
            }
            card.EnchantInternal(enchantment, saved.Enchantment.Amount);
            enchantment.ModifyCard();
            card.FinalizeUpgradeInternal();
        }
        for (int i = 0; i < saved.CurrentUpgradeLevel; i++)
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }
        return card;
    }

    public static RunRngSetSnapshot ToSnapshot(SavedRunRng saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var snapshot = new RunRngSetSnapshot { Seed = saved.Seed };
        foreach (var (name, value) in saved.Rngs)
        {
            if (!TryRunStream(name, out var type))
            {
                throw new FormatException($"Unknown run rng stream '{name}'.");
            }
            snapshot.Rngs[type] = value;
        }
        return snapshot;
    }

    public static PlayerRngSetSnapshot ToSnapshot(SavedPlayerRng saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var snapshot = new PlayerRngSetSnapshot { Seed = saved.Seed };
        foreach (var (name, value) in saved.Rngs)
        {
            if (!TryPlayerStream(name, out var type))
            {
                throw new FormatException($"Unknown player rng stream '{name}'.");
            }
            snapshot.Rngs[type] = value;
        }
        return snapshot;
    }

    public static ActInstance ToActInstance(SavedAct saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var act = new ActInstance(ActRegistry.Get(saved.Id));
        var rooms = act.Rooms;
        var save = saved.Rooms;
        rooms.events.AddRange(save.EventIds);
        rooms.eventsVisited = save.EventsVisited;
        rooms.normalEncounters.AddRange(save.NormalEncounterIds.Select(EncounterRegistry.Get));
        rooms.normalEncountersVisited = save.NormalEncountersVisited;
        rooms.eliteEncounters.AddRange(save.EliteEncounterIds.Select(EncounterRegistry.Get));
        rooms.eliteEncountersVisited = save.EliteEncountersVisited;
        rooms.bossEncountersVisited = save.BossEncountersVisited;
        rooms.Boss = save.BossId != null ? EncounterRegistry.Get(save.BossId) : null;
        rooms.SecondBoss = save.SecondBossId != null ? EncounterRegistry.Get(save.SecondBossId) : null;
        rooms.AncientId = save.AncientId;
        return act;
    }

    public static MapPointHistoryEntry ToHistoryEntry(SavedMapPointHistoryEntry saved) => ToHistoryEntry(saved, 0);

    public static MapPointHistoryEntry ToHistoryEntry(SavedMapPointHistoryEntry saved, int actIndex)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var entry = new MapPointHistoryEntry(saved.MapPointType)
        {
            ActIndex = actIndex,
            GoldGained = saved.GoldGained ?? 0
        };
        foreach (var room in saved.Rooms)
        {
            entry.Rooms.Add(new MapPointRoomHistoryEntry(room.RoomType, room.ModelId, room.MonsterIds.ToArray(), room.TurnsTaken));
        }
        return entry;
    }

    public static void FillProperties(object model, SavedProperties props)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(props);
        var missing = MissingProperties(model.GetType(), props);
        if (missing.Count > 0)
        {
            throw new NotSupportedException($"{model.GetType().Name} has no [SavedProperty] member for {string.Join(", ", missing)}.");
        }
        foreach (var (name, value) in props.Ints)
        {
            var member = SavedMember(model.GetType(), name)!;
            member.SetValue(model, member.PropertyType.IsEnum ? Enum.ToObject(member.PropertyType, value) : value);
        }
        foreach (var (name, value) in props.Bools)
        {
            SavedMember(model.GetType(), name)!.SetValue(model, value);
        }
        foreach (var (name, value) in props.Strings)
        {
            SavedMember(model.GetType(), name)!.SetValue(model, value);
        }
        foreach (var (name, value) in props.IntArrays)
        {
            SavedMember(model.GetType(), name)!.SetValue(model, value.ToArray());
        }
        foreach (var (name, value) in props.ModelIds)
        {
            SavedMember(model.GetType(), name)!.SetValue(model, value);
        }
    }

    public static List<string> MissingProperties(Type type, SavedProperties props)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(props);
        var missing = new List<string>();
        void Need(string name, Type valueType)
        {
            var member = SavedMember(type, name);
            if (member == null || (member.PropertyType != valueType && !(valueType == typeof(int) && member.PropertyType.IsEnum)))
            {
                missing.Add(name);
            }
        }
        foreach (var p in props.Ints)
        {
            Need(p.Key, typeof(int));
        }
        foreach (var p in props.Bools)
        {
            Need(p.Key, typeof(bool));
        }
        foreach (var p in props.Strings)
        {
            Need(p.Key, typeof(string));
        }
        foreach (var p in props.IntArrays)
        {
            Need(p.Key, typeof(int[]));
        }
        foreach (var p in props.ModelIds)
        {
            Need(p.Key, typeof(string));
        }
        foreach (var p in props.Cards)
        {
            missing.Add(p.Key);
        }
        foreach (var p in props.CardArrays)
        {
            missing.Add(p.Key);
        }
        return missing;
    }

    private static PropertyInfo? SavedMember(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite
            || (property.GetCustomAttribute<SavedPropertyAttribute>(true) == null && !SavedPropertyRegistry.IsRegistered(type, name)))
        {
            return null;
        }
        return property;
    }

    private static void CheckPlayer(SavedPlayer player, Action<string> add)
    {
        if (!CardPools.AllCharacterIds.Contains(player.CharacterId, StringComparer.Ordinal))
        {
            add($"character {player.CharacterId} is not supported");
        }
        if (player.CurrentHp <= 0 || player.CurrentHp > player.MaxHp)
        {
            add($"player hp {player.CurrentHp}/{player.MaxHp} is not a live player");
        }
        foreach (var name in player.Rng.Rngs.Keys)
        {
            if (!TryPlayerStream(name, out _))
            {
                add($"player rng stream {name} is unknown");
            }
        }
        foreach (var card in player.Deck)
        {
            if (!CardRegistry.TryGet(card.Id, out var def) || def is UnportedCardDef)
            {
                add($"card {card.Id} is not ported");
                continue;
            }
            if (card.CurrentUpgradeLevel > def.MaxUpgradeLevel)
            {
                add($"card {card.Id} upgrade level {card.CurrentUpgradeLevel} exceeds {def.MaxUpgradeLevel}");
            }
            if (card.Props != null && !card.Props.IsEmpty)
            {
                var extra = new CardInstance(def, 0).Extra;
                var missing = extra == null ? card.Props.Ints.Select(p => p.Key).ToList() : MissingProperties(extra.GetType(), card.Props);
                if (extra == null || missing.Count > 0)
                {
                    add($"card {card.Id} saved properties {string.Join(", ", AllNames(card.Props))} are not mapped");
                }
            }
            var ench = card.Enchantment;
            if (ench != null)
            {
                if (!EnchantmentRegistry.Models.Contains(ench.Id))
                {
                    add($"enchantment {ench.Id} is not ported");
                }
                else if (ench.Props != null && MissingProperties(EnchantmentRegistry.TypeOf(ench.Id), ench.Props).Count > 0)
                {
                    add($"enchantment {ench.Id} saved properties {string.Join(", ", AllNames(ench.Props))} are not mapped");
                }
            }
        }
        foreach (var relic in player.Relics)
        {
            if (!RelicRegistry.Models.Contains(relic.Id))
            {
                add($"relic {relic.Id} is not ported");
            }
            else if (relic.Props != null && MissingProperties(RelicRegistry.TypeOf(relic.Id), relic.Props).Count > 0)
            {
                add($"relic {relic.Id} saved properties {string.Join(", ", AllNames(relic.Props))} are not mapped");
            }
        }
        foreach (var potion in player.Potions)
        {
            if (!PotionRegistry.Models.Contains(potion.Id))
            {
                add($"potion {potion.Id} is not ported");
            }
            if (potion.SlotIndex < 0 || potion.SlotIndex >= player.MaxPotionSlotCount)
            {
                add($"potion {potion.Id} slot {potion.SlotIndex} is outside {player.MaxPotionSlotCount} slots");
            }
        }
    }

    private static IEnumerable<string> AllNames(SavedProperties props)
        => props.Ints.Select(p => p.Key)
            .Concat(props.Bools.Select(p => p.Key))
            .Concat(props.Strings.Select(p => p.Key))
            .Concat(props.IntArrays.Select(p => p.Key))
            .Concat(props.ModelIds.Select(p => p.Key))
            .Concat(props.Cards.Select(p => p.Key))
            .Concat(props.CardArrays.Select(p => p.Key));

    private static void CheckEncounter(string id, Action<string> add)
    {
        if (!EncounterRegistry.Defs.Contains(id))
        {
            add($"encounter {id} is not ported");
        }
    }

    private static void CheckEvent(string id, Action<string> add)
    {
        if (!EventRegistry.Models.Contains(id))
        {
            add($"event {id} is not ported");
        }
    }

    private static bool TryRunStream(string name, out RunRngType type)
    {
        foreach (var value in Enum.GetValues<RunRngType>())
        {
            if (string.Equals(RunRngSet.StreamName(value), name, StringComparison.Ordinal))
            {
                type = value;
                return true;
            }
        }
        type = default;
        return false;
    }

    private static bool TryPlayerStream(string name, out PlayerRngType type)
    {
        foreach (var value in Enum.GetValues<PlayerRngType>())
        {
            if (string.Equals(PlayerRngSet.StreamName(value), name, StringComparison.Ordinal))
            {
                type = value;
                return true;
            }
        }
        type = default;
        return false;
    }

    private static void RequireSchema(SavedRun save)
    {
        if (save.SchemaVersion != SavedRun.SupportedSchemaVersion)
        {
            throw new NotSupportedException($"Run save schema_version {save.SchemaVersion} is not supported.");
        }
    }

    private static void LoadUnknownMapPointOdds(UnknownMapPointOdds odds, SavedRunOdds saved)
    {
        odds.MonsterOdds = saved.UnknownMapPointMonsterOddsValue;
        odds.EliteOdds = saved.UnknownMapPointEliteOddsValue;
        odds.TreasureOdds = saved.UnknownMapPointTreasureOddsValue;
        odds.ShopOdds = saved.UnknownMapPointShopOddsValue;
    }

    private static RelicGrabBag LoadRelicGrabBag(SavedRelicGrabBag saved, bool refreshAllowed)
    {
        var bag = new RelicGrabBag(refreshAllowed);
        bag.LoadFromSerializable(saved.RelicIdLists.Select(kv => new KeyValuePair<RelicRarity, IReadOnlyList<string>>(kv.Key, kv.Value)));
        return bag;
    }

    private static ActMap LoadSavedMap(RunState rs, SavedActMapData saved)
    {
        var serializable = new SerializableActMap
        {
            Points = saved.Points.Select(ToSerializablePoint).ToList(),
            BossPoint = ToSerializablePoint(saved.Boss ?? throw new InvalidOperationException("saved_map has no boss point.")),
            SecondBossPoint = saved.SecondBoss == null ? null : ToSerializablePoint(saved.SecondBoss),
            StartingPoint = ToSerializablePoint(saved.Start ?? throw new InvalidOperationException("saved_map has no starting point.")),
            StartMapPointCoords = new List<MapCoord>(saved.StartCoords),
            GridWidth = saved.Width,
            GridHeight = saved.Height
        };
        return new SavedActMap(serializable);
    }

    private static SerializableMapPoint ToSerializablePoint(SavedMapPoint saved) => new()
    {
        Coord = saved.Coord,
        PointType = saved.Type,
        CanBeModified = saved.CanModify,
        ChildCoords = new List<MapCoord>(saved.Children)
    };

    public static AbstractRoom? CreatePreFinishedRoom(RunState rs, SavedRoom? saved)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (saved == null)
        {
            return null;
        }
        switch (saved.RoomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
            {
                if (saved.ExtraRewards.Count > 0)
                {
                    throw new NotSupportedException("Saved combat rooms with extra rewards are not supported.");
                }
                var encounter = EncounterRegistry.Get(saved.EncounterId ?? throw new InvalidOperationException("A saved combat room has no encounter id."));
                var room = new CombatRoom(encounter, saved.RoomType)
                {
                    GoldProportion = (decimal)saved.GoldProportion,
                    ShouldResumeParentEventAfterCombat = saved.ShouldResumeParentEvent,
                    ParentEventId = saved.ParentEventId
                };
                room.LoadEncounterCustomState(saved.EncounterState);
                if (saved.IsPreFinished)
                {
                    room.MarkPreFinished();
                }
                return room;
            }
            case RoomType.Event:
            {
                var room = new EventRoom(EventRegistry.Create(saved.EventId ?? throw new InvalidOperationException("A saved event room has no event id.")));
                if (saved.IsPreFinished)
                {
                    room.MarkPreFinished();
                }
                return room;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(saved), saved.RoomType, "A pre-finished room is a combat or an event room.");
        }
    }
}
