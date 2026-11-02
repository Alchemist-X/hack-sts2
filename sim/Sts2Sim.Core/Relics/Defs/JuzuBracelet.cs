namespace Sts2Sim.Core.Relics;

public sealed class JuzuBracelet : RelicModel
{
    public const string RelicId = "JUZU_BRACELET";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)
    {
        var val = new HashSet<RoomType>();
        foreach (var current in roomTypes)
        {
            val.Add(current);
        }
        val.Remove(RoomType.Monster);
        return val;
    }
}
