namespace Sts2Sim.Core.Run;

public static class RewardsCmd
{
    public static void OfferForRoomEnd(RunState rs, PlayerState player, AbstractRoom room)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(room);
        RewardsSet rewardsSet;
        if (room is CombatRoom combatRoom && !RewardsSet.ShouldGiveRewards(combatRoom.Encounter))
        {
            rewardsSet = new RewardsSet(player).EmptyForRoom(room);
        }
        else
        {
            rewardsSet = new RewardsSet(player).WithRewardsFromRoom(rs, room);
        }
        rewardsSet.Offer(rs);
    }

    public static void OfferCustom(RunState rs, PlayerState player, List<Reward> rewards)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        new RewardsSet(player).WithCustomRewards(rewards).Offer(rs);
    }

    public static RewardsSet GenerateForRoomEnd(RunState rs, PlayerState player, AbstractRoom room)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var set = new RewardsSet(player).WithRewardsFromRoom(rs, room);
        set.GenerateWithoutOffering(rs);
        return set;
    }

    public static RewardsSet GenerateCustom(RunState rs, PlayerState player, List<Reward> rewards)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var set = new RewardsSet(player).WithCustomRewards(rewards);
        set.GenerateWithoutOffering(rs);
        return set;
    }
}
