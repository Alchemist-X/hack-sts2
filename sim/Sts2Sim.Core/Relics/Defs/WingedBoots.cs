namespace Sts2Sim.Core.Relics;

public sealed class WingedBoots : RelicModel
{
    public const string RelicId = "WINGED_BOOTS";

    private const string RoomsKey = "Rooms";

    private const int RoomCount = 3;

    private int _timesUsed;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool IsUsedUp => TimesUsed >= RoomCount;

    public override bool ShowCounter => !IsUsedUp;

    public override int DisplayAmount => RoomCount - TimesUsed;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(RoomsKey, 3m)
    };

    public int TimesUsed
    {
        get => _timesUsed;
        set
        {
            _timesUsed = value;
            DynamicVars[RoomsKey].BaseValue = RoomCount - _timesUsed;
            CheckIfUsedUp();
        }
    }

    public override bool IsAllowed(RunState rs) => rs.Players.Count == 1;

    public override bool ShouldAllowFreeTravel(RunState rs) => !IsUsedUp;

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (IsUsedUp)
        {
            return;
        }
        if (rs.CurrentRooms.Count > 1)
        {
            return;
        }
        var visitedMapCoords = rs.VisitedMapCoords;
        if (visitedMapCoords.Count <= 1)
        {
            return;
        }
        var map = rs.Map;
        if (map == null)
        {
            return;
        }
        var coord = visitedMapCoords[visitedMapCoords.Count - 2];
        var point = map.GetPoint(coord);
        if (point == null)
        {
            return;
        }
        var currentCoord = rs.CurrentMapCoord;
        var currentMapPoint = currentCoord.HasValue ? map.GetPoint(currentCoord.Value) : null;
        if (currentMapPoint == null)
        {
            return;
        }
        if (point.Children.Contains(currentMapPoint))
        {
            return;
        }
        TimesUsed++;
    }

    private void CheckIfUsedUp()
    {
        if (IsUsedUp)
        {
            Status = RelicStatus.Disabled;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_timesUsed);
    }
}
