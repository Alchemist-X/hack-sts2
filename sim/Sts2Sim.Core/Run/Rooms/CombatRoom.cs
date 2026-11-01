namespace Sts2Sim.Core.Run;

public sealed partial class CombatRoom : AbstractRoom
{
    private readonly RoomType _roomType;

    public CombatRoom(EncounterDef encounter) : this(encounter, EncounterRoomTypes.Of(encounter.Id))
    {
    }

    public CombatRoom(EncounterDef encounter, RoomType roomType)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (roomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
        {
            throw new ArgumentOutOfRangeException(nameof(roomType), roomType, "A combat room is a Monster, Elite or Boss room.");
        }
        Encounter = encounter;
        _roomType = roomType;
    }

    public EncounterDef Encounter { get; }

    public override RoomType RoomType => _roomType;

    public override string? ModelId => Encounter.Id;

    public bool ShouldGiveRewards => EncounterRoomTypes.ShouldGiveRewards(Encounter.Id);

    public decimal GoldProportion { get; set; } = 1m;

    public List<Reward> ExtraRewards { get; private set; } = new();

    public bool ShouldCreateCombat { get; init; } = true;

    public bool ShouldResumeParentEventAfterCombat { get; set; } = true;

    public string? ParentEventId { get; set; }

    public EncounterExtraState? EncounterExtra { get; private set; }

    public void AddExtraReward(Reward reward)
    {
        ArgumentNullException.ThrowIfNull(reward);
        ExtraRewards.Add(reward);
    }

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        RejectRoomStackReconstruction(isRestoringRoomStackBase, nameof(CombatRoom));
        if (IsPreFinished)
        {
            StartPreFinishedCombat(rs);
            return;
        }
        StartCombat(rs);
    }

    public CombatState StartCombat(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!ShouldCreateCombat)
        {
            throw new NotSupportedException("CombatRoom.ShouldCreateCombat = false (combat transitioned in place from an event) is not ported.");
        }
        var player = rs.Players[0];
        var cs = CombatLoop.SetUpCombat(rs, player, Encounter, RunContext(rs));
        var history = RoomStack.CurrentMapPointHistoryEntry(rs);
        if (history != null && history.Rooms.Count > 0 && history.Rooms[^1].MonsterIds is List<string> monsterIds)
        {
            var monsters = cs.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                monsterIds.Add(monsters[i].Def.Id);
            }
        }
        Hook.AfterRoomEntered(rs, this);
        CombatLoop.StartCombat(cs);
        return cs;
    }

    public static CombatRunContext RunContext(RunState rs)
    {
        var coord = RoomStack.CurrentMapCoord(rs);
        return new CombatRunContext(rs.CurrentActIndex, rs.TotalFloor, coord?.col, coord?.row);
    }

    private void StartPreFinishedCombat(RunState rs)
    {
        if (ShouldGiveRewards)
        {
            OfferRoomEndRewards(rs);
        }
    }

    public void OnCombatEnded(RunState rs, CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(cs);
        GoldProportion = (decimal)Encounter.CalculateGoldProportion(cs);
        EncounterExtra = cs.EncounterExtra?.DeepClone();
    }

    public void AfterCombatVictory(RunState rs, CombatState cs)
    {
        OnCombatEnded(rs, cs);
        MarkPreFinished();
    }

    public void OfferRoomEndRewards(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var players = rs.Players;
        var sets = new List<RewardsSet>(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            sets.Add(RewardsCmd.GenerateForRoomEnd(rs, players[i], this));
        }
        for (int i = 0; i < sets.Count; i++)
        {
            Hook.BeforeCombatRewardOffered(rs, sets[i], this);
            sets[i].Offer(rs);
        }
    }

    public override void Exit(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        rs.Combat = null;
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom) => throw new InvalidOperationException("CombatRoom cannot be resumed.");

    public override AbstractRoom CloneFor(RunState rs)
    {
        var clone = CopyBaseInto(new CombatRoom(Encounter, _roomType)
        {
            ShouldCreateCombat = ShouldCreateCombat,
            GoldProportion = GoldProportion,
            ShouldResumeParentEventAfterCombat = ShouldResumeParentEventAfterCombat,
            ParentEventId = ParentEventId,
            EncounterExtra = EncounterExtra?.DeepClone(),
        });
        var extra = ExtraRewards;
        if (extra.Count > 0)
        {
            var player = rs.Players[0];
            clone.ExtraRewards = new List<Reward>(extra.Count);
            for (int i = 0; i < extra.Count; i++)
            {
                clone.ExtraRewards.Add(extra[i].CloneFor(player));
            }
        }
        return clone;
    }

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteDecimal(GoldProportion);
        w.WriteBool(ShouldCreateCombat);
        w.WriteBool(ShouldResumeParentEventAfterCombat);
        w.WriteString(ParentEventId);
        w.WriteBool(EncounterExtra != null);
        EncounterExtra?.WriteCanonical(w);
        var extra = ExtraRewards;
        w.WriteCount(extra.Count);
        for (int i = 0; i < extra.Count; i++)
        {
            extra[i].WriteCanonical(w);
        }
    }
}
