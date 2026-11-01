namespace Sts2Sim.Core.Run;

public static class RunLoop
{
    public const string ActSelectionRngName = "act_selection";

    public const string Act1Overgrowth = "overgrowth";

    public const string Act1Underdocks = "underdocks";

    public const string OvergrowthActId = "OVERGROWTH";

    public const string UnderdocksActId = "UNDERDOCKS";

    public const string NeowEpochId = "NEOW_EPOCH";

    public const string DarvEpochId = "DARV_EPOCH";

    public const string DarvEventId = "DARV";

    public const string TheArchitectEventId = "THE_ARCHITECT";

    public const string BossEncounterSuffix = "_BOSS";

    public const int StartingPotionSlots = 3;

    public const int SinglePlayerSlot = 0;

    public sealed record CharacterStart(string CharacterId, int StartingHp, int StartingGold, int MaxEnergy, int BaseOrbSlotCount, IReadOnlyList<string> StartingDeck, IReadOnlyList<string> StartingRelics);

    public static readonly CharacterStart Ironclad = new(StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, 99, StartingDecks.IroncladMaxEnergy, 0, StartingDecks.Ironclad, new[] { "BURNING_BLOOD" });

    public static CharacterStart GetCharacterStart(string characterId)
    {
        if (CharacterRegistry.TryGet(characterId, out var def))
        {
            return def.ToCharacterStart();
        }
        if (string.Equals(characterId, Ironclad.CharacterId, StringComparison.Ordinal))
        {
            return Ironclad;
        }
        throw new NotSupportedException($"Character {characterId} has no ported starting data.");
    }

    public static IReadOnlyList<ActModel> SelectActs(RunConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.ActIds is { } ids)
        {
            var given = new List<ActModel>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                given.Add(ActRegistry.Get(ids[i]));
            }
            return given;
        }
        var rng = new Rng(DeterministicHash.GetDeterministicHashCode(config.SeedText), ActSelectionRngName);
        if (ActModel.ActsByIndex.Count == 0)
        {
            throw new NotImplementedException("W3 content: no ActModel defs are ported (Models.Acts/*)");
        }
        var acts = ActModel.GetRandomList(rng, config.Unlocks, config.Progress, isMultiplayer: false).ToList();
        var act1 = GetAct1(config.Act1);
        if (act1 != null && acts.Count > 0)
        {
            acts[0] = act1;
        }
        return acts;
    }

    private static ActModel? GetAct1(string act1Key) => act1Key switch
    {
        Act1Overgrowth => ActRegistry.Get(OvergrowthActId),
        Act1Underdocks => ActRegistry.Get(UnderdocksActId),
        _ => null
    };

    public static RunState CreateForNewRun(RunConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var acts = SelectActs(config);
        var start = GetCharacterStart(config.CharacterId);
        var player = new PlayerState(CombatLoop.PlayerCombatId, start.CharacterId, start.StartingHp, start.StartingHp, start.MaxEnergy)
        {
            Gold = start.StartingGold,
            BaseOrbSlotCount = start.BaseOrbSlotCount
        };
        var rng = new RunRngSet(config.SeedText);
        var rs = RunState.CreateStandalone(player, rng, config.Ascension, config.Unlocks);
        rs.Config = config;
        var deck = StartingDecks.ApplyAscensionEffectsToDeck(start.StartingDeck, config.Ascension);
        for (int i = 0; i < deck.Count; i++)
        {
            var card = new CardInstance(CardRegistry.Get(deck[i]), rs.AllocateCardInstanceId());
            card.AttachTo(player);
            card.Def.AfterCreated(card);
            player.Deck.Add(card);
        }
        for (int i = 0; i < start.StartingRelics.Count; i++)
        {
            player.AddRelicInternal(RelicRegistry.Create(start.StartingRelics[i]));
        }
        player.RelicGrabBag = new RelicGrabBag(refreshAllowed: false);
        for (int i = 0; i < acts.Count; i++)
        {
            rs.Acts.Add(new ActInstance(acts[i]));
        }
        rs.Odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
        rs.SharedRelicGrabBag = new RelicGrabBag(refreshAllowed: true);
        player.PlayerRng = PlayerRngSet.ForPlayer(config.SeedText, SinglePlayerSlot);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, config.Ascension);
        InitializeNewRun(rs);
        GenerateRooms(rs);
        return rs;
    }

    private static void InitializeNewRun(RunState rs)
    {
        var content = rs.Config?.Content ?? ContentFilter.All;
        var sharedPool = RelicPools.Get(RelicPools.SharedRelicPool).AllRelicIds.Where(content.AllowsRelic).ToList();
        rs.SharedRelicGrabBag!.Populate(sharedPool, rs.Rng.UpFront);
        for (int i = 0; i < rs.Players.Count; i++)
        {
            var player = rs.Players[i];
            var bag = player.RelicGrabBag!;
            if (!bag.IsPopulated)
            {
                bag.Populate(rs, player, rs.Rng.UpFront);
            }
        }
        rs.ExtraFields.StartedWithNeow = rs.Unlocks.IsEpochRevealed(NeowEpochId);
    }

    private static void GenerateRooms(RunState rs)
    {
        var shared = new List<string>();
        if (rs.Unlocks.IsEpochRevealed(DarvEpochId))
        {
            shared.Add(DarvEventId);
        }
        shared.UnstableShuffle(rs.Rng.UpFront);
        for (int i = 1; i < rs.Acts.Count; i++)
        {
            int count = rs.Rng.UpFront.NextInt(shared.Count + 1);
            var subset = shared.Take(count).ToList();
            shared = shared.Except(subset).ToList();
            rs.Acts[i].SetSharedAncientSubset(subset);
        }
        for (int i = 0; i < rs.Acts.Count; i++)
        {
            var act = rs.Acts[i];
            act.GenerateRooms(rs.Rng.UpFront, rs.Unlocks, rs.Players.Count > 1);
            act.ApplyDiscoveryOrderModifications(rs.Unlocks);
            if (i == rs.Acts.Count - 1 && rs.Ascension >= (int)AscensionLevel.DoubleBoss)
            {
                var bossId = act.BossEncounter.Id;
                var candidates = act.Def.GenerateAllEncounters().Where(e => e.Id.EndsWith(BossEncounterSuffix, StringComparison.Ordinal) && e.Id != bossId).ToList();
                act.Rooms.SecondBoss = rs.Rng.UpFront.NextItem(candidates);
            }
        }
    }

    public static void Begin(RunState rs, RunLoopState loop)
    {
        FinalizeStartingRelics(rs);
        EnterAct(rs, 0, loop);
    }

    private static void FinalizeStartingRelics(RunState rs)
    {
        for (int i = 0; i < rs.Players.Count; i++)
        {
            var relics = rs.Players[i].Relics.ToArray();
            for (int j = 0; j < relics.Length; j++)
            {
                relics[j].AfterObtained(rs);
            }
        }
    }

    public static void EnterAct(RunState rs, int actIndex) => EnterAct(rs, actIndex, null);

    public static void EnterAct(RunState rs, int actIndex, RunLoopState? loop)
    {
        RoomStack.ExitCurrentRooms(rs);
        SetActInternal(rs, actIndex);
        if (loop != null)
        {
            loop.MapOpen = false;
            loop.OpenCardReward = RunLoopState.NoCardReward;
        }
        if (actIndex == 0 && rs.ExtraFields.StartedWithNeow)
        {
            EnterMapCoord(rs, rs.Map!.StartingMapPoint.coord);
        }
        else
        {
            RoomStack.EnterRoom(rs, new MapRoom());
            if (loop != null)
            {
                loop.MapOpen = true;
            }
        }
        Hook.AfterActEntered(rs);
    }

    private static void SetActInternal(RunState rs, int actIndex)
    {
        if (rs.CurrentActIndex != actIndex)
        {
            rs.CurrentActIndex = actIndex;
            rs.NextRoomId = 0;
        }
        rs.VisitedMapCoords.Clear();
        rs.ActFloor = 0;
        rs.Odds!.UnknownMapPoint.ResetToBase();
        GenerateMap(rs);
    }

    public static void GenerateMap(RunState rs)
    {
        var map = rs.Act.Def.CreateMap(rs, replaceTreasureWithElites: false);
        map = Hook.ModifyGeneratedMap(rs, map, rs.CurrentActIndex);
        Hook.AfterMapGenerated(rs, map, rs.CurrentActIndex);
        if (!rs.ExtraFields.StartedWithNeow && rs.CurrentActIndex == 0)
        {
            map.StartingMapPoint.PointType = MapPointType.Monster;
        }
        rs.Map = map;
        rs.VisitedMapCoords.RemoveAll(coord => !map.HasPoint(coord));
    }

    public static void GenerateMapOnLoad(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (rs.Map is not SavedActMap saved)
        {
            GenerateMap(rs);
            return;
        }
        var map = Hook.ModifyGeneratedMapLate(rs, saved, rs.CurrentActIndex);
        Hook.AfterMapGenerated(rs, map, rs.CurrentActIndex);
        rs.Map = map;
        rs.VisitedMapCoords.RemoveAll(coord => !map.HasPoint(coord));
    }

    public static bool AddVisitedMapCoord(RunState rs, MapCoord coord)
    {
        if (rs.VisitedMapCoords.Contains(coord))
        {
            return false;
        }
        rs.VisitedMapCoords.Add(coord);
        rs.NextRoomId = 0;
        return true;
    }

    public static MapCoord? CurrentMapCoord(RunState rs) => rs.VisitedMapCoords.Count == 0 ? null : rs.VisitedMapCoords[^1];

    public static MapPoint? CurrentMapPoint(RunState rs) => CurrentMapCoord(rs) is { } coord ? rs.Map?.GetPoint(coord) : null;

    public static MapPointHistoryEntry? CurrentMapPointHistoryEntry(RunState rs) => rs.MapPointHistory.Count == 0 ? null : rs.MapPointHistory[^1];

    public static void EnterMapCoord(RunState rs, MapCoord coord)
    {
        if (!AddVisitedMapCoord(rs, coord))
        {
            return;
        }
        EnterMapCoordInternal(rs, coord, null);
    }

    private static void EnterMapCoordInternal(RunState rs, MapCoord coord, AbstractRoom? preFinishedRoom)
    {
        var point = rs.Map!.GetPoint(coord) ?? throw new InvalidOperationException($"{coord} is not on the current map.");
        EnterMapPointInternal(rs, coord.row + 1, point.PointType, preFinishedRoom);
    }

    public static void EnterMapPointInternal(RunState rs, int actFloor, MapPointType pointType, AbstractRoom? preFinishedRoom)
    {
        rs.ActFloor = actFloor;
        RoomStack.ExitCurrentRooms(rs);
        RoomType roomType;
        if (pointType == MapPointType.Unknown && preFinishedRoom != null)
        {
            roomType = RoomType.Monster;
        }
        else
        {
            var children = CurrentMapPoint(rs)?.Children ?? new HashSet<MapPoint>();
            var blacklist = BuildRoomTypeBlacklist(CurrentMapPointHistoryEntry(rs), children);
            roomType = RollRoomTypeFor(rs, pointType, blacklist);
        }
        var room = preFinishedRoom ?? RoomStack.CreateRoom(rs, roomType, pointType);
        if (preFinishedRoom == null)
        {
            AppendToMapPointHistory(rs, pointType, room.RoomType, room.ModelId);
        }
        if (room is CombatRoom { IsPreFinished: true, ParentEventId: { } parentId })
        {
            RoomStack.EnterRoomInternal(rs, new EventRoom(EventRegistry.Create(parentId)), isRestoringRoomStackBase: true);
            RoomStack.EnterRoomInternal(rs, room, isRestoringRoomStackBase: false);
            return;
        }
        RoomStack.EnterRoom(rs, room);
    }

    public static HashSet<RoomType> BuildRoomTypeBlacklist(MapPointHistoryEntry? previousMapPointEntry, IReadOnlyCollection<MapPoint> nextMapPoints)
    {
        var blacklist = new HashSet<RoomType>();
        if ((previousMapPointEntry != null && previousMapPointEntry.HasRoomOfType(RoomType.Shop)) || (nextMapPoints.Count > 0 && nextMapPoints.All(p => p.PointType == MapPointType.Shop)))
        {
            blacklist.Add(RoomType.Shop);
        }
        return blacklist;
    }

    public static RoomType RollRoomTypeFor(RunState rs, MapPointType pointType, IEnumerable<RoomType> blacklist)
    {
        if (TryGetRoomTypeForTutorial(rs, pointType, out var tutorial))
        {
            return tutorial;
        }
        return pointType switch
        {
            MapPointType.Unassigned => RoomType.Unassigned,
            MapPointType.Unknown => rs.Odds!.UnknownMapPoint.Roll(blacklist, rs),
            MapPointType.Shop => RoomType.Shop,
            MapPointType.Treasure => RoomType.Treasure,
            MapPointType.RestSite => RoomType.RestSite,
            MapPointType.Monster => RoomType.Monster,
            MapPointType.Elite => RoomType.Elite,
            MapPointType.Boss => RoomType.Boss,
            MapPointType.Ancient => RoomType.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(pointType), pointType, null)
        };
    }

    private static bool TryGetRoomTypeForTutorial(RunState rs, MapPointType pointType, out RoomType roomType)
    {
        roomType = RoomType.Unassigned;
        if (rs.Players.Count > 1 || pointType != MapPointType.Unassigned)
        {
            return false;
        }
        var progress = rs.Config?.Progress ?? RunProfileProgress.Veteran;
        if (progress.NumberOfRuns > 0)
        {
            return false;
        }
        if (rs.MapPointHistory.Any(e => e.MapPointType == MapPointType.Unassigned))
        {
            return false;
        }
        roomType = RoomType.Event;
        return true;
    }

    public static void AppendToMapPointHistory(RunState rs, MapPointType mapPointType, RoomType initialRoomType, string? roomModelId)
    {
        var entry = new MapPointHistoryEntry(mapPointType);
        entry.Rooms.Add(new MapPointRoomHistoryEntry(initialRoomType, roomModelId, Array.Empty<string>(), 0));
        rs.MapPointHistory.Add(entry);
    }

    public static void LoadIntoLatestMapCoord(RunState rs, AbstractRoom? preFinishedRoom)
    {
        if (CurrentMapCoord(rs) is { } coord)
        {
            EnterMapCoordInternal(rs, coord, preFinishedRoom);
            return;
        }
        RoomStack.EnterRoom(rs, new MapRoom());
    }

    public static void ProceedFromTerminalRewardsScreen(RunState rs)
    {
        if (rs.CurrentRooms.Count > 1 && RoomStack.CurrentRoom(rs) is CombatRoom { ShouldResumeParentEventAfterCombat: true })
        {
            RoomStack.ResumePreviousRoom(rs);
        }
    }

    public static bool ResumesParentEvent(RunState rs)
        => rs.CurrentRooms.Count > 1 && RoomStack.CurrentRoom(rs) is CombatRoom { ShouldResumeParentEventAfterCombat: true };

    public static void MoveToNextAct(RunState rs) => MoveToNextAct(rs, null);

    public static void MoveToNextAct(RunState rs, RunLoopState? loop)
    {
        rs.ActFloor++;
        EnterNextAct(rs, loop);
    }

    private static void EnterNextAct(RunState rs, RunLoopState? loop)
    {
        if (rs.CurrentActIndex >= rs.Acts.Count - 1)
        {
            if (RoomStack.CurrentRoom(rs) is { IsVictoryRoom: true })
            {
                throw new InvalidOperationException("EnterNextAct called at victory room! WinRun should be called instead");
            }
            if (loop != null)
            {
                loop.MapOpen = false;
                loop.OpenCardReward = RunLoopState.NoCardReward;
            }
            RoomStack.EnterRoom(rs, new EventRoom(EventRegistry.Create(TheArchitectEventId)));
            return;
        }
        EnterAct(rs, rs.CurrentActIndex + 1, loop);
    }

    public static bool IsGameOver(RunState rs) => rs.Players.Count > 0 && rs.Players.All(p => p.IsDead);

    public static void AfterAction(RunState rs, RunLoopState loop)
    {
        var cs = rs.Combat;
        if (cs != null && !cs.IsInProgress && !cs.IsStarting && cs.Outcome == CombatOutcome.Victory)
        {
            OnCombatVictory(rs, loop, cs);
        }
        loop.Phase = ResolvePhase(rs, loop);
    }

    private static void OnCombatVictory(RunState rs, RunLoopState loop, CombatState cs)
    {
        if (RoomStack.CurrentRoom(rs) is not CombatRoom room)
        {
            rs.Combat = null;
            return;
        }
        int turnsTaken = cs.Player.TurnNumber;
        if (CurrentMapPointHistoryEntry(rs) is { Rooms.Count: > 0 } entry)
        {
            entry.Rooms[^1] = entry.Rooms[^1] with { TurnsTaken = turnsTaken };
        }
        room.AfterCombatVictory(rs, cs);
        rs.Combat = null;
        loop.MapOpen = false;
        loop.OpenCardReward = RunLoopState.NoCardReward;
        int before = rs.RewardsStack.Count;
        room.OfferRoomEndRewards(rs);
        if (rs.RewardsStack.Count == before)
        {
            ProceedTerminal(rs, loop);
        }
    }

    public static void ProceedTerminal(RunState rs, RunLoopState loop)
    {
        loop.OpenCardReward = RunLoopState.NoCardReward;
        var room = RoomStack.CurrentRoom(rs);
        if (room != null && (room.RoomType == RoomType.Boss || room.IsVictoryRoom))
        {
            var map = rs.Map!;
            if (map.SecondBossMapPoint != null && CurrentMapCoord(rs) == map.BossMapPoint.coord)
            {
                ProceedFromTerminalRewardsScreen(rs);
                loop.MapOpen = true;
                return;
            }
            MoveToNextAct(rs, loop);
            return;
        }
        bool resumes = ResumesParentEvent(rs);
        ProceedFromTerminalRewardsScreen(rs);
        loop.MapOpen = !resumes;
    }

    public static RunPhase ResolvePhase(RunState rs, RunLoopState loop)
    {
        if (IsGameOver(rs))
        {
            return RunPhase.Defeat;
        }
        var cs = rs.Combat;
        if (cs != null && (cs.IsInProgress || cs.IsStarting))
        {
            return RunPhase.Combat;
        }
        var room = RoomStack.CurrentRoom(rs);
        if (loop.Victory || room is { IsVictoryRoom: true })
        {
            loop.Victory = true;
            return RunPhase.Victory;
        }
        if (rs.RewardsStack.Count > 0)
        {
            return loop.OpenCardReward >= 0 ? RunPhase.CardReward : RunPhase.Rewards;
        }
        if (loop.MapOpen)
        {
            return RunPhase.Map;
        }
        return room switch
        {
            null => RunPhase.Map,
            MapRoom => RunPhase.Map,
            MerchantRoom => RunPhase.Shop,
            RestSiteRoom => RunPhase.RestSite,
            TreasureRoom => RunPhase.Treasure,
            EventRoom => RunPhase.Event,
            _ => RunPhase.Map
        };
    }
}
