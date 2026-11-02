namespace Sts2Sim.Core.Relics;

public sealed class WhiteStar : RelicModel
{
    public const string RelicId = "WHITE_STAR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room == null || room.RoomType != RoomType.Elite)
        {
            return false;
        }
        rewards.Add(new CardReward(player, CardCreationOptions.ForRoom(rs, Owner, RoomType.Boss), 3));
        return true;
    }
}
