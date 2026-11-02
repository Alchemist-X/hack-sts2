namespace Sts2Sim.Core.Run;

public sealed class TreasureRoom : AbstractRoom
{
    public TreasureRoom(int actIndex)
    {
        if (actIndex < 0 || actIndex > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(actIndex), "must be between 0 and 2");
        }
        ActIndex = actIndex;
    }

    public int ActIndex { get; }

    public override RoomType RoomType => RoomType.Treasure;

    public List<string>? CurrentRelicIds { get; private set; }

    public string? CurrentRelicId => CurrentRelicIds is { Count: > 0 } ids ? ids[0] : null;

    public bool ChestOpened { get; private set; }

    public bool SingleplayerSkipped { get; private set; }

    public bool RelicClaimed { get; private set; }

    public bool IsRelicPickingOpen => ChestOpened && CurrentRelicIds != null && !SingleplayerSkipped;

    public bool ProceedAllowed => ChestOpened;

    public int GoldGained { get; private set; }

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        RejectRoomStackReconstruction(isRestoringRoomStackBase, nameof(TreasureRoom));
        Hook.AfterRoomEntered(rs, this);
        BeginRelicPicking(rs);
    }

    private void BeginRelicPicking(RunState rs)
    {
        if (CurrentRelicIds != null)
        {
            throw new InvalidOperationException("Attempted to start new relic picking session while one was already occurring!");
        }
        CurrentRelicIds = new List<string>();
        var players = rs.Players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (Hook.ShouldGenerateTreasure(rs, player))
            {
                var rarity = RelicFactory.RollRarity(rs.Rng.TreasureRoomRelics);
                var bag = rs.SharedRelicGrabBag ?? throw new InvalidOperationException("The treasure room needs the run's shared relic grab bag.");
                CurrentRelicIds.Add(bag.PullFromFront(rarity, rs) ?? RelicFactory.FallbackRelicId);
            }
        }
        if (CurrentRelicIds.Count == 0)
        {
            EndRelicVoting();
        }
    }

    public void OpenChest(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (ChestOpened)
        {
            throw new InvalidOperationException("The chest is already open.");
        }
        GoldGained = DoNormalRewards(rs);
        DoExtraRewardsIfNeeded(rs);
        ChestOpened = true;
        if (CurrentRelicIds == null)
        {
            CompleteWithNoRelics();
        }
    }

    private int DoNormalRewards(RunState rs)
    {
        var player = rs.Players[0];
        if (!Hook.ShouldGenerateTreasure(rs, player))
        {
            return 0;
        }
        var playerRng = player.PlayerRng ?? throw new InvalidOperationException("The treasure room needs the player's rng set.");
        double gold = playerRng.Rewards.NextInt(42, 53);
        if (rs.Ascension >= (int)AscensionLevel.Poverty)
        {
            gold *= PovertyAscensionGoldMultiplier;
        }
        PlayerCmd.GainGold(rs, (int)gold, player);
        gold += TryHandleSpoilsMap(rs, player);
        return (int)gold;
    }

    public const double PovertyAscensionGoldMultiplier = 0.75;

    private static int TryHandleSpoilsMap(RunState rs, PlayerState player)
    {
        var coord = RoomStack.CurrentMapCoord(rs);
        if (coord == null || rs.Map?.GetPoint(coord.Value) == null)
        {
            return 0;
        }
        if (!rs.Map.GetQuests(coord.Value).Contains(SpoilsMapId))
        {
            return 0;
        }
        var maps = player.Deck.Where(c => c.Id == SpoilsMapId).ToList();
        int num = 0;
        foreach (var current in maps)
        {
            num += Sts2Sim.Core.Cards.SpoilsMap.OnQuestComplete(rs, current);
        }
        return num;
    }

    public const string SpoilsMapId = "SPOILS_MAP";

    private void DoExtraRewardsIfNeeded(RunState rs)
    {
        var players = rs.Players;
        var sets = new List<RewardsSet>(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            sets.Add(RewardsCmd.GenerateForRoomEnd(rs, players[i], this));
        }
        for (int i = 0; i < sets.Count; i++)
        {
            if (sets[i].Rewards.Count > 0)
            {
                RoomStack.Offer(rs, sets[i]);
            }
        }
    }

    public void TakeRelic(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!IsRelicPickingOpen)
        {
            throw new InvalidOperationException("No relic to take.");
        }
        var relicId = CurrentRelicIds![0];
        RelicCmd.Obtain(rs, relicId, rs.Players[0]);
        RelicClaimed = true;
        EndRelicVoting();
    }

    public void SkipRelic(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!IsRelicPickingOpen)
        {
            throw new InvalidOperationException("No relic to skip.");
        }
        if (rs.Players.Count == 1)
        {
            SingleplayerSkipped = true;
        }
    }

    private void EndRelicVoting()
    {
        CurrentRelicIds = null;
        SingleplayerSkipped = false;
    }

    private void CompleteWithNoRelics()
    {
        CurrentRelicIds = null;
    }

    public override void Exit(RunState rs)
    {
        if (SingleplayerSkipped)
        {
            EndRelicVoting();
        }
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom) => throw new InvalidOperationException("TreasureRoom cannot be resumed.");

    public override AbstractRoom CloneFor(RunState rs) => CopyBaseInto(new TreasureRoom(ActIndex)
    {
        CurrentRelicIds = CurrentRelicIds == null ? null : new List<string>(CurrentRelicIds),
        ChestOpened = ChestOpened,
        SingleplayerSkipped = SingleplayerSkipped,
        RelicClaimed = RelicClaimed,
        GoldGained = GoldGained,
    });

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Treasure);
        w.WriteInt32(ActIndex);
        var ids = CurrentRelicIds;
        w.WriteBool(ids != null);
        if (ids != null)
        {
            w.WriteCount(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                w.WriteString(ids[i]);
            }
        }
        w.WriteBool(ChestOpened);
        w.WriteBool(SingleplayerSkipped);
        w.WriteBool(RelicClaimed);
        w.WriteInt32(GoldGained);
    }
}
